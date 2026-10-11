using FluentAssertions;
using Articles.Application.Admin;
using Articles.Application.Posts;
using Articles.Application.Posts.Dtos;
using Articles.Domain.Entities;
using Articles.Infrastructure.Persistence;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Articles.Tests.Admin;

public class ActivityEntryTests
{
    [Fact]
    public void Text_longer_than_its_column_is_shortened_instead_of_failing()
    {
        var entry = new ActivityEntry(DateTimeOffset.UtcNow, "owner@example.com", "Deleted post", new string('t', 1000));

        entry.Subject.Should().HaveLength(ActivityEntry.MaxSubjectLength).And.EndWith("…");
    }

    [Fact]
    public void A_missing_subject_is_stored_as_empty_and_spaces_are_trimmed()
    {
        var entry = new ActivityEntry(DateTimeOffset.UtcNow, "  owner@example.com ", " Signed in ", null);

        (entry.Actor, entry.Action, entry.Subject).Should().Be(("owner@example.com", "Signed in", ""));
    }
}

public class ActivityAndExportTests : IDisposable
{
    private const string Owner = "owner@example.com";

    private readonly TestDatabase _fixture = new();
    private readonly ManualTimeProvider _clock = new(new DateTimeOffset(2026, 6, 15, 10, 0, 0, TimeSpan.Zero));
    private readonly ActivityLog _log;
    private readonly PostService _posts;
    private readonly AdminService _admin;

    public ActivityAndExportTests()
    {
        _log = new ActivityLog(_fixture.Db, NullLogger<ActivityLog>.Instance, _clock);
        _posts = new PostService(_fixture.Posts, _fixture.Tags, _fixture.Engagement, _fixture.Db);
        _admin = new AdminService(_fixture.Admin, _fixture.Posts, _fixture.Comments, _fixture.Db, NullLogger<AdminService>.Instance, _clock, _log);
    }

    private async Task<Guid> PublishedAsync(Guid authorId, string title, params string[] tags)
    {
        var draft = await _posts.CreateDraftAsync(new CreatePostRequest(title, "A subtitle", TestDatabase.Document($"Body of {title}."), null, tags), authorId);
        await _posts.PublishAsync(draft.Id, authorId);
        return draft.Id;
    }

    private async Task<IReadOnlyList<ActivityDto>> HistoryAsync() => (await _log.GetPageAsync(1, 100)).Items;

    // ---- History ------------------------------------------------------------------------------

    [Fact]
    public async Task History_is_listed_newest_first_with_who_what_and_when()
    {
        await _log.RecordAsync(Owner, Activity.SignedIn);
        _clock.Advance(TimeSpan.FromMinutes(5));
        await _log.RecordAsync(Owner, Activity.DeletedPost, "An old post");

        var history = await HistoryAsync();

        history.Select(h => h.Action).Should().Equal(Activity.DeletedPost, Activity.SignedIn);
        history[0].Should().BeEquivalentTo(new { Actor = Owner, Subject = "An old post", At = _clock.GetUtcNow() });
    }

    [Fact]
    public async Task History_is_paged()
    {
        for (var i = 0; i < 5; i++)
        {
            await _log.RecordAsync(Owner, Activity.DeletedTopic, $"topic-{i}");
            _clock.Advance(TimeSpan.FromSeconds(1));
        }

        var second = await _log.GetPageAsync(2, 2);

        second.TotalCount.Should().Be(5);
        second.Items.Select(i => i.Subject).Should().Equal("topic-2", "topic-1");
    }

    [Fact]
    public async Task Taking_a_post_down_and_putting_it_back_are_both_recorded_in_plain_words()
    {
        var ada = await _fixture.AddUserAsync("ada");
        var id = await PublishedAsync(ada.Id, "Controversial");

        await _admin.SetPostStatusAsync(id, "Inactive", Owner);
        _clock.Advance(TimeSpan.FromMinutes(1));
        await _admin.SetPostStatusAsync(id, "Published", Owner);

        var history = await HistoryAsync();
        history.Select(h => (h.Action, h.Subject)).Should().Equal(
            (Activity.ChangedPostStatus, "Controversial: Not active to Published"),
            (Activity.ChangedPostStatus, "Controversial: Published to Not active"));
        history.Should().OnlyContain(h => h.Actor == Owner);
    }

    [Fact]
    public async Task Deleting_a_post_and_changing_topics_are_recorded()
    {
        var ada = await _fixture.AddUserAsync("ada");
        var id = await PublishedAsync(ada.Id, "Short lived", "news", "tech");
        var news = (await _fixture.Tags.GetBySlugAsync("news"))!;
        var tech = (await _fixture.Tags.GetBySlugAsync("tech"))!;

        await _admin.RenameTagAsync(news.Id, "World News", Owner);
        await _admin.MergeTagAsync(tech.Id, news.Id, Owner);
        await _admin.DeletePostAsync(id, Owner);

        (await HistoryAsync()).Select(h => h.Action).Should().BeEquivalentTo(
            [Activity.RenamedTopic, Activity.MergedTopic, Activity.DeletedPost]);
        (await HistoryAsync()).Single(h => h.Action == Activity.DeletedPost).Subject.Should().Be("Short lived");
    }

    [Fact]
    public async Task An_action_that_fails_leaves_no_trace_in_the_history()
    {
        var failure = await Record.ExceptionAsync(() => _admin.SetPostStatusAsync(Guid.NewGuid(), "Published", Owner));

        failure.Should().NotBeNull();
        (await HistoryAsync()).Should().BeEmpty();
    }

    [Fact]
    public async Task History_older_than_the_retention_period_is_cleared_at_the_next_sign_in()
    {
        await _log.RecordAsync(Owner, Activity.DeletedPost, "Long ago");
        _clock.Advance(Activity.Retention + TimeSpan.FromDays(1));
        await _log.RecordAsync(Owner, Activity.DeletedTopic, "Recent");

        (await HistoryAsync()).Should().HaveCount(2, "nothing is cleared until someone signs in");

        await _log.RecordAsync(Owner, Activity.SignedIn);

        (await HistoryAsync()).Select(h => h.Subject).Should().BeEquivalentTo(["Recent", ""]);
    }

    [Fact]
    public async Task A_history_line_that_cannot_be_written_does_not_fail_the_action()
    {
        using var broken = new TestDatabase();
        var log = new ActivityLog(broken.Db, NullLogger<ActivityLog>.Instance, _clock);
        broken.Dispose();

        var act = () => log.RecordAsync(Owner, Activity.SignedIn);

        await act.Should().NotThrowAsync();
    }

    // ---- Export -------------------------------------------------------------------------------

    [Fact]
    public async Task Export_contains_every_post_whatever_its_state_with_its_full_body()
    {
        var ada = await _fixture.AddUserAsync("ada");
        var live = await PublishedAsync(ada.Id, "Live post", "news");
        var hidden = await PublishedAsync(ada.Id, "Hidden post");
        await _admin.SetPostStatusAsync(hidden, "Inactive", Owner);
        var draft = await _posts.CreateDraftAsync(new CreatePostRequest("Just a draft", null, TestDatabase.Document("Not finished."), null, []), ada.Id);

        var export = await _admin.ExportAsync(Owner);

        export.PostCount.Should().Be(3);
        export.ExportedAt.Should().Be(_clock.GetUtcNow());
        export.Posts.Select(p => (p.Id, p.Status)).Should().BeEquivalentTo([(live, "Published"), (hidden, "Inactive"), (draft.Id, "Draft")]);

        var post = export.Posts.Single(p => p.Id == live);
        post.Should().BeEquivalentTo(new { Title = "Live post", Subtitle = "A subtitle", AuthorHandle = "ada", PlainText = "Body of Live post." });
        post.Slug.Should().StartWith("live-post-");
        post.Tags.Should().Equal("news");
        post.ContentJson.Should().Be(TestDatabase.Document("Body of Live post."));
        post.PublishedAt.Should().NotBeNull();
    }

    [Fact]
    public async Task Export_of_an_empty_site_is_an_empty_list_not_an_error()
    {
        var export = await _admin.ExportAsync(Owner);

        export.PostCount.Should().Be(0);
        export.Posts.Should().BeEmpty();
    }

    [Fact]
    public async Task Exporting_is_itself_recorded()
    {
        var ada = await _fixture.AddUserAsync("ada");
        await PublishedAsync(ada.Id, "One");

        await _admin.ExportAsync(Owner);

        (await HistoryAsync()).Should().ContainSingle(h => h.Action == Activity.ExportedPosts && h.Subject == "1 posts" && h.Actor == Owner);
    }

    public void Dispose() => _fixture.Dispose();
}
