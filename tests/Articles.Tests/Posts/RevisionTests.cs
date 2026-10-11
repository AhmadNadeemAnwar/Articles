using FluentAssertions;
using Articles.Application.Posts;
using Articles.Application.Posts.Dtos;
using Articles.Domain.Exceptions;
using Xunit;

namespace Articles.Tests.Posts;

public class RevisionTests : IDisposable
{
    private readonly TestDatabase _fixture = new();
    // Starts at the real time, because a revision is stamped with the real clock when it is created.
    private readonly ManualTimeProvider _clock = new(DateTimeOffset.UtcNow);
    private readonly PostService _service;

    public RevisionTests() => _service = new PostService(_fixture.Posts, _fixture.Tags, _fixture.Engagement, _fixture.Db, _clock);

    private static UpdatePostRequest Edit(string body, string title = "A title") => new(title, null, TestDatabase.Document(body), null, []);

    private async Task<(Guid PostId, Guid AuthorId)> DraftAsync(string body = "Version one.")
    {
        var author = await _fixture.AddUserAsync();
        var draft = await _service.CreateDraftAsync(new CreatePostRequest("A title", null, TestDatabase.Document(body), null, []), author.Id);
        return (draft.Id, author.Id);
    }

    private int StoredRevisions(Guid postId) => _fixture.Db.PostRevisions.Count(r => r.PostId == postId);

    [Fact]
    public async Task The_first_edit_keeps_a_copy_of_what_was_there_before()
    {
        var (postId, authorId) = await DraftAsync("Version one.");

        await _service.UpdateAsync(postId, Edit("Version two."), authorId);

        var revisions = await _service.GetRevisionsAsync(postId, authorId);
        revisions.Should().ContainSingle();
        (await _service.GetRevisionAsync(postId, revisions[0].Id, authorId)).ContentJson.Should().Be(TestDatabase.Document("Version one."));
    }

    [Fact]
    public async Task Saves_seconds_apart_do_not_each_make_a_copy()
    {
        var (postId, authorId) = await DraftAsync();

        for (var i = 2; i <= 8; i++) await _service.UpdateAsync(postId, Edit($"Version {i}."), authorId);

        StoredRevisions(postId).Should().Be(1, "the editor saves every few seconds while someone types");
    }

    [Fact]
    public async Task Another_copy_is_kept_once_enough_time_has_passed()
    {
        var (postId, authorId) = await DraftAsync();
        await _service.UpdateAsync(postId, Edit("Version two."), authorId);

        _clock.Advance(PostService.RevisionInterval + TimeSpan.FromMinutes(1));
        await _service.UpdateAsync(postId, Edit("Version three."), authorId);

        StoredRevisions(postId).Should().Be(2);
        var newest = (await _service.GetRevisionsAsync(postId, authorId))[0];
        (await _service.GetRevisionAsync(postId, newest.Id, authorId)).ContentJson.Should().Be(TestDatabase.Document("Version two."));
    }

    [Fact]
    public async Task Saving_without_changing_the_title_or_body_makes_no_copy()
    {
        var (postId, authorId) = await DraftAsync("Same words.");

        await _service.UpdateAsync(postId, new UpdatePostRequest("A title", "Only the subtitle changed", TestDatabase.Document("Same words."), null, ["news"]), authorId);

        StoredRevisions(postId).Should().Be(0);
    }

    [Fact]
    public async Task A_change_of_title_alone_is_worth_a_copy()
    {
        var (postId, authorId) = await DraftAsync("Same words.");

        await _service.UpdateAsync(postId, Edit("Same words.", title: "A better title"), authorId);

        (await _service.GetRevisionsAsync(postId, authorId)).Should().ContainSingle().Which.Title.Should().Be("A title");
    }

    [Fact]
    public async Task Only_the_newest_copies_are_kept()
    {
        var (postId, authorId) = await DraftAsync();
        const int extra = 4;

        for (var i = 0; i < PostService.MaxRevisionsPerPost + extra; i++)
        {
            _clock.Advance(PostService.RevisionInterval + TimeSpan.FromMinutes(1));
            await _service.UpdateAsync(postId, Edit($"Edit number {i}."), authorId);
        }

        StoredRevisions(postId).Should().Be(PostService.MaxRevisionsPerPost);
        var listed = await _service.GetRevisionsAsync(postId, authorId, 100);
        listed.Should().HaveCount(PostService.MaxRevisionsPerPost).And.BeInDescendingOrder(r => r.CreatedAt);
    }

    [Fact]
    public async Task Trimming_one_posts_copies_leaves_another_posts_alone()
    {
        var (first, authorId) = await DraftAsync();
        var second = (await _service.CreateDraftAsync(new CreatePostRequest("Other", null, TestDatabase.Document("Other."), null, []), authorId)).Id;
        await _service.UpdateAsync(second, Edit("Other, edited.", "Other"), authorId);

        await _fixture.Posts.TrimRevisionsAsync(first, 0);

        StoredRevisions(second).Should().Be(1);
    }

    [Fact]
    public async Task Someone_else_cannot_read_your_copies()
    {
        var (postId, authorId) = await DraftAsync();
        await _service.UpdateAsync(postId, Edit("Version two."), authorId);
        var revisionId = (await _service.GetRevisionsAsync(postId, authorId))[0].Id;
        var stranger = await _fixture.AddUserAsync("stranger");

        var act = () => _service.GetRevisionAsync(postId, revisionId, stranger.Id);

        await act.Should().ThrowAsync<ForbiddenException>();
    }

    [Fact]
    public async Task A_copy_cannot_be_fetched_through_a_different_post()
    {
        var (first, authorId) = await DraftAsync();
        await _service.UpdateAsync(first, Edit("Version two."), authorId);
        var revisionId = (await _service.GetRevisionsAsync(first, authorId))[0].Id;
        var second = (await _service.CreateDraftAsync(new CreatePostRequest("Other", null, TestDatabase.Document("Other."), null, []), authorId)).Id;

        (await Record.ExceptionAsync(() => _service.GetRevisionAsync(second, revisionId, authorId))).Should().BeOfType<NotFoundException>();
        (await Record.ExceptionAsync(() => _service.GetRevisionAsync(first, Guid.NewGuid(), authorId))).Should().BeOfType<NotFoundException>();
    }

    public void Dispose() => _fixture.Dispose();
}
