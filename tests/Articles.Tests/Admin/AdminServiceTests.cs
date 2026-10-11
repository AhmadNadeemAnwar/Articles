using FluentAssertions;
using Articles.Application.Admin;
using Articles.Application.Comments;
using Articles.Application.Comments.Dtos;
using Articles.Application.Engagement;
using Articles.Application.Posts;
using Articles.Application.Posts.Dtos;
using Articles.Domain.Entities;
using Articles.Domain.Exceptions;
using Articles.Infrastructure.Persistence.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Articles.Tests.Admin;

public class AdminServiceTests : IDisposable
{
    private readonly TestDatabase _fixture = new();
    private readonly ManualTimeProvider _clock = new(new DateTimeOffset(2026, 6, 15, 10, 0, 0, TimeSpan.Zero));
    private readonly PostService _posts;
    private readonly CommentService _comments;
    private readonly EngagementService _engagement;
    private readonly ReactionService _reactions;
    private readonly AdminService _admin;

    public AdminServiceTests()
    {
        _posts = new PostService(_fixture.Posts, _fixture.Tags, _fixture.Engagement, _fixture.Db);
        _comments = new CommentService(_fixture.Comments, _fixture.Posts, _fixture.Db);
        _engagement = new EngagementService(_fixture.Engagement, _fixture.Posts, _fixture.Users, _fixture.Tags, _fixture.Db);
        _reactions = new ReactionService(new ReactionRepository(_fixture.Db), _fixture.Posts, _clock);
        _admin = new AdminService(_fixture.Admin, _fixture.Posts, _fixture.Comments, _fixture.Db, NullLogger<AdminService>.Instance, _clock);
    }

    private async Task<Guid> PublishedAsync(Guid authorId, string title, params string[] tags)
    {
        var draft = await _posts.CreateDraftAsync(
            new CreatePostRequest(title, null, TestDatabase.Document($"Body of {title}."), null, tags), authorId);
        await _posts.PublishAsync(draft.Id, authorId);
        return draft.Id;
    }

    private async Task<Guid> DraftAsync(Guid authorId, string title) =>
        (await _posts.CreateDraftAsync(new CreatePostRequest(title, null, TestDatabase.Document("Draft body."), null, []), authorId)).Id;

    private async Task<int> PostCountAsync(string slug) =>
        (await _fixture.Tags.GetBySlugAsync(slug))!.PostCount;

    // ---- Stats ---------------------------------------------------------------------------

    [Fact]
    public async Task Stats_count_posts_by_status_and_totals_across_the_site()
    {
        var ada = await _fixture.AddUserAsync("ada");
        var bob = await _fixture.AddUserAsync("bob");
        var first = await PublishedAsync(ada.Id, "First", "news");
        await PublishedAsync(bob.Id, "Second", "news", "tech");
        await DraftAsync(ada.Id, "Unfinished");

        for (var i = 0; i < 7; i++) await _reactions.ToggleAsync(first, Guid.NewGuid().ToString(), "clap");
        for (var i = 0; i < 3; i++) await _reactions.ToggleAsync(first, Guid.NewGuid().ToString(), "insightful");
        await _engagement.ToggleBookmarkAsync(first, bob.Id);
        await _comments.AddAsync(first, new CreateCommentRequest("Nice", null), bob.Id);

        var stats = await _admin.GetStatsAsync();

        stats.PublishedPosts.Should().Be(2);
        stats.DraftPosts.Should().Be(1);
        stats.InactivePosts.Should().Be(0);
        stats.Users.Should().Be(2);
        stats.Comments.Should().Be(1);
        stats.Claps.Should().Be(7);
        stats.Insightful.Should().Be(3);
        stats.Bookmarks.Should().Be(1);
        stats.Tags.Should().Be(2);
    }

    [Fact]
    public async Task Stats_chart_covers_the_last_thirty_days_with_quiet_days_as_zero()
    {
        var ada = await _fixture.AddUserAsync("ada");
        await PublishedAsync(ada.Id, "Today one");
        await PublishedAsync(ada.Id, "Today two");

        // Publishing stamps the real clock, so the test clock follows it to keep "today" meaningful.
        var chartClock = new ManualTimeProvider(DateTimeOffset.UtcNow);
        var admin = new AdminService(_fixture.Admin, _fixture.Posts, _fixture.Comments, _fixture.Db, NullLogger<AdminService>.Instance, chartClock);

        var stats = await admin.GetStatsAsync();

        stats.PublishedLast30Days.Should().HaveCount(30);
        stats.PublishedLast30Days.Last().Date.Should().Be(DateTime.UtcNow.ToString("yyyy-MM-dd"));
        stats.PublishedLast30Days.Last().Count.Should().Be(2);
        stats.PublishedLast30Days.Take(29).Should().OnlyContain(d => d.Count == 0);
        stats.PublishedLast30Days.Select(d => d.Date).Should().BeInAscendingOrder().And.OnlyHaveUniqueItems();
    }

    [Fact]
    public async Task Stats_rank_the_top_posts_by_views_and_by_claps()
    {
        var ada = await _fixture.AddUserAsync("ada");
        var reader = await _fixture.AddUserAsync("reader");
        var quiet = await PublishedAsync(ada.Id, "Quiet");
        var loud = await PublishedAsync(ada.Id, "Loud");
        var viewed = await PublishedAsync(ada.Id, "Viewed");

        for (var i = 0; i < 9; i++) await _reactions.ToggleAsync(loud, Guid.NewGuid().ToString(), "clap");
        var viewedPost = await _fixture.Posts.GetByIdAsync(viewed);
        for (var i = 0; i < 5; i++) viewedPost!.RegisterView();
        await _fixture.Db.SaveChangesAsync();

        var stats = await _admin.GetStatsAsync();

        stats.TopByClaps.First().Title.Should().Be("Loud");
        stats.TopByViews.First().Title.Should().Be("Viewed");
        stats.TopByViews.First().Views.Should().Be(5);
        stats.TopByClaps.Select(p => p.Id).Should().Contain(quiet);
    }

    [Fact]
    public async Task Stats_on_an_empty_site_are_all_zero_without_error()
    {
        var stats = await _admin.GetStatsAsync();

        stats.PublishedPosts.Should().Be(0);
        stats.Views.Should().Be(0);
        stats.Claps.Should().Be(0);
        stats.TopByViews.Should().BeEmpty();
        stats.PublishedLast30Days.Should().HaveCount(30);
    }

    // ---- Posts ---------------------------------------------------------------------------

    [Fact]
    public async Task The_post_list_includes_drafts_and_every_authors_posts()
    {
        var ada = await _fixture.AddUserAsync("ada");
        var bob = await _fixture.AddUserAsync("bob");
        await PublishedAsync(ada.Id, "Ada published");
        await DraftAsync(bob.Id, "Bob draft");

        var all = await _admin.GetPostsAsync(null, null, 1, 25);

        all.TotalCount.Should().Be(2);
        all.Items.Select(p => p.Status).Should().BeEquivalentTo("Published", "Draft");
        all.Items.Select(p => p.AuthorHandle).Should().BeEquivalentTo("ada", "bob");
    }

    [Fact]
    public async Task The_post_list_filters_by_status_and_text()
    {
        var ada = await _fixture.AddUserAsync("ada");
        await PublishedAsync(ada.Id, "Gardening notes");
        await PublishedAsync(ada.Id, "Cooking notes");
        await DraftAsync(ada.Id, "Gardening draft");

        (await _admin.GetPostsAsync("Draft", null, 1, 25)).Items.Single().Title.Should().Be("Gardening draft");
        (await _admin.GetPostsAsync("published", null, 1, 25)).TotalCount.Should().Be(2);
        (await _admin.GetPostsAsync(null, "GARDENING", 1, 25)).TotalCount.Should().Be(2, "search ignores case");
        (await _admin.GetPostsAsync(null, "ada", 1, 25)).TotalCount.Should().Be(3, "search also matches the author's handle");
    }

    [Fact]
    public async Task An_unknown_status_filter_is_rejected()
    {
        var act = () => _admin.GetPostsAsync("Archived", null, 1, 25);

        await act.Should().ThrowAsync<DomainException>();
    }

    [Fact]
    public async Task Taking_a_post_down_makes_it_inactive_and_releases_its_tag_counts()
    {
        var ada = await _fixture.AddUserAsync("ada");
        var id = await PublishedAsync(ada.Id, "Controversial", "news", "tech");
        (await PostCountAsync("news")).Should().Be(1);

        await _admin.UnpublishPostAsync(id, "owner@example.com");

        (await _fixture.Posts.GetByIdAsync(id))!.Status.ToString().Should().Be("Inactive");
        (await _admin.GetStatsAsync()).InactivePosts.Should().Be(1);
        (await PostCountAsync("news")).Should().Be(0);
        (await PostCountAsync("tech")).Should().Be(0);
    }

    [Fact]
    public async Task Taking_down_twice_is_refused_and_an_unknown_post_is_not_found()
    {
        var ada = await _fixture.AddUserAsync("ada");
        var id = await PublishedAsync(ada.Id, "Once is enough");
        await _admin.UnpublishPostAsync(id, "x");

        (await Record.ExceptionAsync(() => _admin.UnpublishPostAsync(id, "x"))).Should().BeOfType<DomainException>();
        (await Record.ExceptionAsync(() => _admin.UnpublishPostAsync(Guid.NewGuid(), "x"))).Should().BeOfType<NotFoundException>();
    }

    [Fact]
    public async Task An_admin_can_put_any_post_back_up_and_its_tag_counts_return()
    {
        var ada = await _fixture.AddUserAsync("ada");
        var id = await PublishedAsync(ada.Id, "Back and forth", "news");
        var slug = (await _fixture.Posts.GetByIdAsync(id))!.Slug;
        await _admin.SetPostStatusAsync(id, "Inactive", "owner@example.com");

        await _admin.SetPostStatusAsync(id, "Published", "owner@example.com");

        var post = await _fixture.Posts.GetByIdAsync(id);
        post!.Status.ToString().Should().Be("Published");
        post.Slug.Should().Be(slug);
        (await PostCountAsync("news")).Should().Be(1);
    }

    [Fact]
    public async Task An_admin_publishing_a_never_published_draft_gives_it_an_address()
    {
        var ada = await _fixture.AddUserAsync("ada");
        var draft = await DraftAsync(ada.Id, "First Time Out");

        await _admin.SetPostStatusAsync(draft, "Published", "owner@example.com");

        (await _fixture.Posts.GetByIdAsync(draft))!.Slug.Should().StartWith("first-time-out-");
    }

    [Theory]
    [InlineData("Unlisted")]
    [InlineData("Deleted")]
    [InlineData("")]
    public async Task The_posts_list_and_status_change_accept_only_the_three_states(string status)
    {
        var ada = await _fixture.AddUserAsync("ada");
        var id = await PublishedAsync(ada.Id, "Guarded");

        (await Record.ExceptionAsync(() => _admin.SetPostStatusAsync(id, status, "x"))).Should().BeOfType<DomainException>();
        if (status.Length > 0)
            (await Record.ExceptionAsync(() => _admin.GetPostsAsync(status, null, 1, 10))).Should().BeOfType<DomainException>();
    }

    [Fact]
    public async Task Deleting_a_published_post_removes_it_with_its_comments_and_releases_tag_counts()
    {
        var ada = await _fixture.AddUserAsync("ada");
        var bob = await _fixture.AddUserAsync("bob");
        var id = await PublishedAsync(ada.Id, "Doomed", "news");
        await _comments.AddAsync(id, new CreateCommentRequest("A comment", null), bob.Id);

        await _admin.DeletePostAsync(id, "owner@example.com");

        (await _fixture.Posts.GetByIdAsync(id)).Should().BeNull();
        (await _fixture.Db.Comments.CountAsync()).Should().Be(0);
        (await PostCountAsync("news")).Should().Be(0);
    }

    [Fact]
    public async Task Deleting_a_draft_leaves_tag_counts_alone()
    {
        var ada = await _fixture.AddUserAsync("ada");
        await PublishedAsync(ada.Id, "Live", "news");
        var draft = (await _posts.CreateDraftAsync(
            new CreatePostRequest("Draft", null, TestDatabase.Document("body"), null, ["news"]), ada.Id)).Id;

        await _admin.DeletePostAsync(draft, "owner@example.com");

        (await PostCountAsync("news")).Should().Be(1, "only the published post counts");
    }

    // ---- Comments ------------------------------------------------------------------------

    [Fact]
    public async Task The_comment_list_spans_all_posts_newest_first_with_post_context()
    {
        var ada = await _fixture.AddUserAsync("ada");
        var bob = await _fixture.AddUserAsync("bob");
        var one = await PublishedAsync(ada.Id, "Post one");
        var two = await PublishedAsync(ada.Id, "Post two");
        await _comments.AddAsync(one, new CreateCommentRequest("Older", null), bob.Id);
        await Task.Delay(5);
        await _comments.AddAsync(two, new CreateCommentRequest("Newer", null), bob.Id);

        var page = await _admin.GetCommentsAsync(1, 25);

        page.Items.Select(c => c.Body).Should().Equal("Newer", "Older");
        page.Items.First().PostTitle.Should().Be("Post two");
        page.Items.First().AuthorHandle.Should().Be("bob");
    }

    [Fact]
    public async Task Removing_a_comment_blanks_it_and_lowers_the_posts_count_once()
    {
        var ada = await _fixture.AddUserAsync("ada");
        var bob = await _fixture.AddUserAsync("bob");
        var post = await PublishedAsync(ada.Id, "Post");
        var comment = await _comments.AddAsync(post, new CreateCommentRequest("Spam", null), bob.Id);
        (await _fixture.Posts.GetByIdAsync(post))!.CommentCount.Should().Be(1);

        await _admin.DeleteCommentAsync(comment.Id, "owner@example.com");
        await _admin.DeleteCommentAsync(comment.Id, "owner@example.com");

        var stored = await _fixture.Comments.GetByIdAsync(comment.Id);
        stored!.IsDeleted.Should().BeTrue();
        stored.Body.Should().BeEmpty();
        (await _fixture.Posts.GetByIdAsync(post))!.CommentCount.Should().Be(0, "a second removal must not drive the count negative or double-subtract");
    }

    [Fact]
    public async Task Removing_an_unknown_comment_is_not_found()
    {
        (await Record.ExceptionAsync(() => _admin.DeleteCommentAsync(Guid.NewGuid(), "x"))).Should().BeOfType<NotFoundException>();
    }

    // ---- Tags ----------------------------------------------------------------------------

    [Fact]
    public async Task The_tag_list_shows_post_counts_and_followers()
    {
        var ada = await _fixture.AddUserAsync("ada");
        var reader = await _fixture.AddUserAsync("reader");
        await PublishedAsync(ada.Id, "One", "news");
        await PublishedAsync(ada.Id, "Two", "news");
        await _engagement.ToggleFollowTagAsync("news", reader.Id);

        var tags = await _admin.GetTagsAsync();

        var news = tags.Single(t => t.Slug == "news");
        news.PostCount.Should().Be(2);
        news.Followers.Should().Be(1);
    }

    [Fact]
    public async Task Renaming_a_tag_changes_the_name_but_never_the_slug()
    {
        var ada = await _fixture.AddUserAsync("ada");
        await PublishedAsync(ada.Id, "One", "news");
        var id = (await _fixture.Tags.GetBySlugAsync("news"))!.Id;

        var renamed = await _admin.RenameTagAsync(id, "  Breaking News ", "owner@example.com");

        renamed.Name.Should().Be("Breaking News");
        renamed.Slug.Should().Be("news", "existing /tag/news links must keep working");
    }

    [Fact]
    public async Task Renaming_rejects_blank_and_overlong_names()
    {
        var ada = await _fixture.AddUserAsync("ada");
        await PublishedAsync(ada.Id, "One", "news");
        var id = (await _fixture.Tags.GetBySlugAsync("news"))!.Id;

        (await Record.ExceptionAsync(() => _admin.RenameTagAsync(id, "  ", "x"))).Should().BeOfType<DomainException>();
        (await Record.ExceptionAsync(() => _admin.RenameTagAsync(id, new string('x', 41), "x"))).Should().BeOfType<DomainException>();
    }

    [Fact]
    public async Task Merging_moves_posts_and_followers_without_duplicates_and_recounts()
    {
        var ada = await _fixture.AddUserAsync("ada");
        var u1 = await _fixture.AddUserAsync("u1");
        var u2 = await _fixture.AddUserAsync("u2");

        await PublishedAsync(ada.Id, "P1", "old");
        var p2 = await PublishedAsync(ada.Id, "P2", "old", "new");
        await PublishedAsync(ada.Id, "P3", "new");
        // A draft carrying the old tag must come across too, but must not be counted as published.
        await _posts.CreateDraftAsync(new CreatePostRequest("P4", null, TestDatabase.Document("draft"), null, ["old"]), ada.Id);

        await _engagement.ToggleFollowTagAsync("old", u1.Id);
        await _engagement.ToggleFollowTagAsync("new", u1.Id);
        await _engagement.ToggleFollowTagAsync("old", u2.Id);

        var oldId = (await _fixture.Tags.GetBySlugAsync("old"))!.Id;
        var newId = (await _fixture.Tags.GetBySlugAsync("new"))!.Id;

        await _admin.MergeTagAsync(oldId, newId, "owner@example.com");

        (await _fixture.Tags.GetBySlugAsync("old")).Should().BeNull("the source tag is gone");
        (await PostCountAsync("new")).Should().Be(3, "P1, P2 and P3 are published; the draft P4 is not counted");

        var postsWithNew = await _fixture.Db.PostTags.Where(pt => pt.TagId == newId).Select(pt => pt.PostId).ToListAsync();
        postsWithNew.Should().HaveCount(4, "all four posts now carry the target tag");
        postsWithNew.Should().OnlyHaveUniqueItems("P2 had both tags and must not be linked twice");
        postsWithNew.Should().Contain(p2);

        var followers = await _fixture.Db.TagFollows.Where(f => f.TagId == newId).Select(f => f.UserId).ToListAsync();
        followers.Should().BeEquivalentTo(new[] { u1.Id, u2.Id }, "u1 already followed both and must not be duplicated");
        (await _fixture.Db.TagFollows.CountAsync(f => f.TagId == oldId)).Should().Be(0);
    }

    [Fact]
    public async Task Merging_a_tag_into_itself_or_a_missing_tag_is_refused()
    {
        var ada = await _fixture.AddUserAsync("ada");
        await PublishedAsync(ada.Id, "P", "one");
        var id = (await _fixture.Tags.GetBySlugAsync("one"))!.Id;

        (await Record.ExceptionAsync(() => _admin.MergeTagAsync(id, id, "x"))).Should().BeOfType<DomainException>();
        (await Record.ExceptionAsync(() => _admin.MergeTagAsync(id, Guid.NewGuid(), "x"))).Should().BeOfType<NotFoundException>();
        (await Record.ExceptionAsync(() => _admin.MergeTagAsync(Guid.NewGuid(), id, "x"))).Should().BeOfType<NotFoundException>();
    }

    [Fact]
    public async Task Deleting_a_tag_removes_its_links_but_keeps_the_posts()
    {
        var ada = await _fixture.AddUserAsync("ada");
        var reader = await _fixture.AddUserAsync("reader");
        var post = await PublishedAsync(ada.Id, "Keeper", "doomed", "stays");
        await _engagement.ToggleFollowTagAsync("doomed", reader.Id);
        var id = (await _fixture.Tags.GetBySlugAsync("doomed"))!.Id;

        await _admin.DeleteTagAsync(id, "owner@example.com");

        (await _fixture.Tags.GetBySlugAsync("doomed")).Should().BeNull();
        (await _fixture.Db.PostTags.CountAsync(pt => pt.TagId == id)).Should().Be(0);
        (await _fixture.Db.TagFollows.CountAsync(f => f.TagId == id)).Should().Be(0);
        (await _fixture.Posts.GetByIdAsync(post)).Should().NotBeNull();
        (await PostCountAsync("stays")).Should().Be(1);
    }

    public void Dispose() => _fixture.Dispose();
}
