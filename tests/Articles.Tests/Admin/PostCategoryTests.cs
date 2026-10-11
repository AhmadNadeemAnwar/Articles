using FluentAssertions;
using Articles.Application.Admin;
using Articles.Application.Admin.Dtos;
using Articles.Application.Posts;
using Articles.Application.Posts.Dtos;
using Articles.Domain.Common;
using Articles.Domain.Entities;
using Articles.Domain.Exceptions;
using Articles.Infrastructure.Persistence;
using Articles.Infrastructure.Persistence.Repositories;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Articles.Tests.Admin;

/// <summary>Moving posts between categories from the admin portal, and finding them by category.</summary>
public class PostCategoryTests : IDisposable
{
    private readonly TestDatabase _fixture = new();
    private readonly ManualTimeProvider _clock = new(new DateTimeOffset(2026, 10, 11, 10, 0, 0, TimeSpan.Zero));
    private readonly PostService _posts;
    private readonly ActivityLog _log;
    private readonly AdminService _admin;
    private readonly SiteRepository _site;

    public PostCategoryTests()
    {
        _posts = new PostService(_fixture.Posts, _fixture.Tags, _fixture.Engagement, _fixture.Db);
        _log = new ActivityLog(_fixture.Db, NullLogger<ActivityLog>.Instance, _clock);
        _site = new SiteRepository(_fixture.Db);
        _admin = new AdminService(_fixture.Admin, _fixture.Posts, _fixture.Comments, _fixture.Db, NullLogger<AdminService>.Instance, _clock, _log, _site);
    }

    public void Dispose() => _fixture.Dispose();

    private async Task<Category> CategoryAsync(string name, int order = 1)
    {
        var category = new Category(name, name.ToLowerInvariant().Replace(' ', '-'), order);
        await _site.AddCategoryAsync(category);
        await _fixture.Db.SaveChangesAsync();
        return category;
    }

    private async Task<Guid> PublishedAsync(Guid authorId, string title)
    {
        var draft = await _posts.CreateDraftAsync(new CreatePostRequest(title, null, TestDatabase.Document($"Body of {title}."), null, []), authorId);
        await _posts.PublishAsync(draft.Id, authorId);
        return draft.Id;
    }

    private async Task<Guid> DraftAsync(Guid authorId, string title) =>
        (await _posts.CreateDraftAsync(new CreatePostRequest(title, null, TestDatabase.Document("Draft body."), null, []), authorId)).Id;

    private async Task<Guid?> CategoryOfAsync(Guid postId) => (await _fixture.Posts.GetByIdAsync(postId))!.CategoryId;

    // ---- Moving ---------------------------------------------------------------------------

    [Fact]
    public async Task A_post_can_be_moved_into_a_category_and_out_of_it_again()
    {
        var ada = await _fixture.AddUserAsync("ada");
        var life = await CategoryAsync("Life");
        var id = await PublishedAsync(ada.Id, "Moving house");

        await _admin.SetPostCategoryAsync(id, life.Id, "owner@example.com");
        (await CategoryOfAsync(id)).Should().Be(life.Id);

        await _admin.SetPostCategoryAsync(id, null, "owner@example.com");
        (await CategoryOfAsync(id)).Should().BeNull();
    }

    [Fact]
    public async Task A_post_can_be_moved_from_one_category_to_another()
    {
        var ada = await _fixture.AddUserAsync("ada");
        var life = await CategoryAsync("Life");
        var tech = await CategoryAsync("Tech", 2);
        var id = await PublishedAsync(ada.Id, "Shelf to shelf");
        await _admin.SetPostCategoryAsync(id, life.Id, "owner@example.com");

        await _admin.SetPostCategoryAsync(id, tech.Id, "owner@example.com");

        (await CategoryOfAsync(id)).Should().Be(tech.Id);
    }

    [Fact]
    public async Task Drafts_and_posts_that_are_not_active_can_be_moved_too()
    {
        var ada = await _fixture.AddUserAsync("ada");
        var life = await CategoryAsync("Life");
        var draft = await DraftAsync(ada.Id, "Unfinished");
        var hidden = await PublishedAsync(ada.Id, "Hidden");
        await _admin.SetPostStatusAsync(hidden, "Inactive", "owner@example.com");

        await _admin.SetPostCategoryAsync(draft, life.Id, "owner@example.com");
        await _admin.SetPostCategoryAsync(hidden, life.Id, "owner@example.com");

        (await CategoryOfAsync(draft)).Should().Be(life.Id);
        (await CategoryOfAsync(hidden)).Should().Be(life.Id);
    }

    [Fact]
    public async Task Moving_a_post_changes_nothing_but_its_category()
    {
        var ada = await _fixture.AddUserAsync("ada");
        var life = await CategoryAsync("Life");
        var id = await PublishedAsync(ada.Id, "Same words");
        var before = (await _fixture.Posts.GetByIdAsync(id))!;
        var (slug, status, content, title) = (before.Slug, before.Status, before.ContentJson, before.Title);

        await _admin.SetPostCategoryAsync(id, life.Id, "owner@example.com");

        var after = (await _fixture.Posts.GetByIdAsync(id))!;
        (after.Slug, after.Status, after.ContentJson, after.Title).Should().Be((slug, status, content, title));
    }

    [Fact]
    public async Task A_category_that_does_not_exist_is_refused_and_the_post_stays_where_it_was()
    {
        var ada = await _fixture.AddUserAsync("ada");
        var life = await CategoryAsync("Life");
        var id = await PublishedAsync(ada.Id, "Stays put");
        await _admin.SetPostCategoryAsync(id, life.Id, "owner@example.com");

        var act = () => _admin.SetPostCategoryAsync(id, Guid.NewGuid(), "owner@example.com");

        (await act.Should().ThrowAsync<DomainException>()).WithMessage("*no longer exists*");
        (await CategoryOfAsync(id)).Should().Be(life.Id);
    }

    [Fact]
    public async Task A_post_that_does_not_exist_is_not_found()
    {
        var act = () => _admin.SetPostCategoryAsync(Guid.NewGuid(), null, "owner@example.com");

        await act.Should().ThrowAsync<NotFoundException>();
    }

    [Fact]
    public async Task Moving_a_post_is_written_in_the_history_with_where_it_went()
    {
        var ada = await _fixture.AddUserAsync("ada");
        var life = await CategoryAsync("Life");
        var tech = await CategoryAsync("Tech", 2);
        var id = await PublishedAsync(ada.Id, "Tracked");

        await _admin.SetPostCategoryAsync(id, life.Id, "owner@example.com");
        await _admin.SetPostCategoryAsync(id, tech.Id, "owner@example.com");
        await _admin.SetPostCategoryAsync(id, null, "owner@example.com");

        var moved = (await _log.GetPageAsync(1, 10)).Items.Where(e => e.Action == Activity.MovedPostCategory).ToList();
        moved.Select(e => e.Subject).Should().BeEquivalentTo(["Tracked: no category to Life", "Tracked: Life to Tech", "Tracked: Tech to no category"]);
        moved.Should().OnlyContain(e => e.Actor == "owner@example.com");
    }

    [Fact]
    public async Task Putting_a_post_where_it_already_is_does_nothing_and_is_not_written_down()
    {
        var ada = await _fixture.AddUserAsync("ada");
        var life = await CategoryAsync("Life");
        var id = await PublishedAsync(ada.Id, "Already there");
        await _admin.SetPostCategoryAsync(id, life.Id, "owner@example.com");
        var before = (await _log.GetPageAsync(1, 50)).TotalCount;

        await _admin.SetPostCategoryAsync(id, life.Id, "owner@example.com");
        await _admin.SetPostCategoryAsync(await PublishedAsync(ada.Id, "Never categorised"), null, "owner@example.com");

        (await _log.GetPageAsync(1, 50)).TotalCount.Should().Be(before);
    }

    // ---- Listing and filtering --------------------------------------------------------------

    [Fact]
    public async Task The_posts_list_says_which_category_each_post_is_in()
    {
        var ada = await _fixture.AddUserAsync("ada");
        var life = await CategoryAsync("Life");
        var inLife = await PublishedAsync(ada.Id, "In life");
        await PublishedAsync(ada.Id, "In nothing");
        await _admin.SetPostCategoryAsync(inLife, life.Id, "owner@example.com");

        var rows = (await _admin.GetPostsAsync(null, null, 1, 25)).Items.ToDictionary(p => p.Title);

        rows["In life"].CategoryId.Should().Be(life.Id);
        rows["In life"].Category.Should().Be("Life");
        rows["In nothing"].CategoryId.Should().BeNull();
        rows["In nothing"].Category.Should().BeNull();
    }

    [Fact]
    public async Task The_posts_list_can_be_filtered_to_one_category_or_to_posts_in_none()
    {
        var ada = await _fixture.AddUserAsync("ada");
        var life = await CategoryAsync("Life");
        var tech = await CategoryAsync("Tech", 2);
        var a = await PublishedAsync(ada.Id, "A");
        var b = await PublishedAsync(ada.Id, "B");
        await PublishedAsync(ada.Id, "C");
        await _admin.SetPostCategoryAsync(a, life.Id, "owner@example.com");
        await _admin.SetPostCategoryAsync(b, tech.Id, "owner@example.com");

        Titles(await _admin.GetPostsAsync(null, null, 1, 25, category: life.Id.ToString())).Should().Equal("A");
        Titles(await _admin.GetPostsAsync(null, null, 1, 25, category: tech.Id.ToString())).Should().Equal("B");
        Titles(await _admin.GetPostsAsync(null, null, 1, 25, category: "none")).Should().Equal("C");
        Titles(await _admin.GetPostsAsync(null, null, 1, 25, category: "NONE")).Should().Equal("C");
        Titles(await _admin.GetPostsAsync(null, null, 1, 25, category: "")).Should().BeEquivalentTo(["A", "B", "C"]);
        Titles(await _admin.GetPostsAsync(null, null, 1, 25)).Should().BeEquivalentTo(["A", "B", "C"]);
    }

    [Fact]
    public async Task The_category_filter_works_together_with_the_status_and_search_filters()
    {
        var ada = await _fixture.AddUserAsync("ada");
        var life = await CategoryAsync("Life");
        var live = await PublishedAsync(ada.Id, "Garden notes");
        var draft = await DraftAsync(ada.Id, "Garden draft");
        await _admin.SetPostCategoryAsync(live, life.Id, "owner@example.com");
        await _admin.SetPostCategoryAsync(draft, life.Id, "owner@example.com");

        Titles(await _admin.GetPostsAsync("Published", null, 1, 25, category: life.Id.ToString())).Should().Equal("Garden notes");
        Titles(await _admin.GetPostsAsync(null, "draft", 1, 25, category: life.Id.ToString())).Should().Equal("Garden draft");
        Titles(await _admin.GetPostsAsync("Draft", null, 1, 25, category: "none")).Should().BeEmpty();
    }

    [Theory]
    [InlineData("nonsense")]
    [InlineData("12345")]
    [InlineData("none; drop table posts")]
    public async Task A_category_filter_that_is_neither_none_nor_an_id_is_refused(string filter)
    {
        var act = () => _admin.GetPostsAsync(null, null, 1, 25, category: filter);

        await act.Should().ThrowAsync<DomainException>();
    }

    [Fact]
    public async Task Deleting_a_category_leaves_its_posts_in_no_category()
    {
        var ada = await _fixture.AddUserAsync("ada");
        var life = await CategoryAsync("Life");
        var id = await PublishedAsync(ada.Id, "Orphan to be");
        await _admin.SetPostCategoryAsync(id, life.Id, "owner@example.com");

        _site.RemoveCategory(life);
        await _fixture.Db.SaveChangesAsync();

        (await CategoryOfAsync(id)).Should().BeNull();
        Titles(await _admin.GetPostsAsync(null, null, 1, 25, category: "none")).Should().Equal("Orphan to be");
    }

    private static List<string> Titles(PagedResult<AdminPostDto> page) => page.Items.Select(p => p.Title).OrderBy(t => t).ToList();
}
