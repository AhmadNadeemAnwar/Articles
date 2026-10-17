using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Articles.Application.Admin;
using Articles.Application.Posts;
using Articles.Application.Posts.Dtos;
using Articles.Domain.Entities;
using Articles.Domain.Exceptions;
using Articles.Infrastructure.Persistence;
using Articles.Infrastructure.Persistence.Repositories;
using Articles.Tests.Api;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Articles.Tests.Posts;

/// <summary>A reader's anonymous note after reacting: what is accepted, and the owner's list of them.</summary>
public class FeedbackTests : IDisposable
{
    private readonly TestDatabase _fixture = new();
    private readonly ActivityLog _log;
    private readonly FeedbackService _service;
    private readonly PostService _posts;

    public FeedbackTests()
    {
        _log = new ActivityLog(_fixture.Db, NullLogger<ActivityLog>.Instance, TimeProvider.System);
        _service = new FeedbackService(new FeedbackRepository(_fixture.Db), _fixture.Db, _log);
        _posts = new PostService(_fixture.Posts, _fixture.Tags, _fixture.Engagement, _fixture.Db);
    }

    public void Dispose() => _fixture.Dispose();

    private async Task<Guid> PostAsync(bool publish = true, string handle = "ada")
    {
        var ada = await _fixture.AddUserAsync(handle);
        var draft = await _posts.CreateDraftAsync(new CreatePostRequest("A post", null, TestDatabase.Document("Body."), null, []), ada.Id);
        if (publish) await _posts.PublishAsync(draft.Id, ada.Id);
        return draft.Id;
    }

    [Fact]
    public async Task A_reader_can_say_what_they_liked_and_add_a_suggestion()
    {
        var post = await PostAsync();

        await _service.SubmitAsync(post, new SubmitFeedbackRequest(["useful", "clear-writing"], "  More examples please.  "));

        var item = (await _service.GetPageAsync(1, 10)).Items.Single();
        item.PostId.Should().Be(post);
        item.PostTitle.Should().Be("A post");
        item.Liked.Should().Equal("clear-writing", "useful");
        item.Text.Should().Be("More examples please.");
    }

    [Fact]
    public async Task Choices_alone_or_words_alone_are_enough()
    {
        var post = await PostAsync();

        await _service.SubmitAsync(post, new SubmitFeedbackRequest(["made-me-think"], null));
        await _service.SubmitAsync(post, new SubmitFeedbackRequest(null, "Lovely."));

        (await _service.GetPageAsync(1, 10)).TotalCount.Should().Be(2);
    }

    [Theory]
    [InlineData(new string[0], "")]
    [InlineData(new string[0], "   ")]
    public async Task Nothing_at_all_is_refused(string[] liked, string text)
    {
        var post = await PostAsync();

        var failure = await Record.ExceptionAsync(() => _service.SubmitAsync(post, new SubmitFeedbackRequest(liked, text)));

        failure.Should().BeOfType<DomainException>();
    }

    [Fact]
    public async Task A_choice_that_is_not_offered_is_refused()
    {
        var post = await PostAsync();

        var failure = await Record.ExceptionAsync(() => _service.SubmitAsync(post, new SubmitFeedbackRequest(["useful", "<script>"], null)));

        failure.Should().BeOfType<DomainException>();
        (await _service.GetPageAsync(1, 10)).TotalCount.Should().Be(0);
    }

    [Fact]
    public async Task Words_over_the_limit_are_refused_and_markup_is_kept_as_plain_text()
    {
        var post = await PostAsync();

        (await Record.ExceptionAsync(() => _service.SubmitAsync(post, new SubmitFeedbackRequest(null, new string('x', ReaderFeedback.MaxTextLength + 1)))))
            .Should().BeOfType<DomainException>();

        await _service.SubmitAsync(post, new SubmitFeedbackRequest(null, "<b>bold</b>\u0007"));
        (await _service.GetPageAsync(1, 10)).Items.Single().Text.Should().Be("<b>bold</b>");
    }

    [Fact]
    public async Task Feedback_is_only_taken_for_a_published_post()
    {
        var draft = await PostAsync(publish: false);

        (await Record.ExceptionAsync(() => _service.SubmitAsync(draft, new SubmitFeedbackRequest(["useful"], null)))).Should().BeOfType<NotFoundException>();
        (await Record.ExceptionAsync(() => _service.SubmitAsync(Guid.NewGuid(), new SubmitFeedbackRequest(["useful"], null)))).Should().BeOfType<NotFoundException>();
    }

    [Fact]
    public async Task The_owner_sees_the_newest_first_and_can_delete_one_which_is_recorded()
    {
        var post = await PostAsync();
        await _service.SubmitAsync(post, new SubmitFeedbackRequest(null, "first"));
        await Task.Delay(5);
        await _service.SubmitAsync(post, new SubmitFeedbackRequest(null, "second"));

        var page = await _service.GetPageAsync(1, 10);
        page.Items.Select(i => i.Text).Should().Equal("second", "first");

        await _service.DeleteAsync(page.Items[0].Id, "owner@example.com");

        (await _service.GetPageAsync(1, 10)).Items.Select(i => i.Text).Should().Equal("first");
        (await _log.GetPageAsync(1, 10)).Items.Should().Contain(e => e.Action == Activity.DeletedFeedback && e.Subject == "on A post");
    }

    [Fact]
    public async Task Deleting_feedback_that_does_not_exist_is_not_found()
    {
        (await Record.ExceptionAsync(() => _service.DeleteAsync(Guid.NewGuid(), "owner@example.com"))).Should().BeOfType<NotFoundException>();
    }

    [Fact]
    public async Task Deleting_a_post_takes_its_feedback_with_it()
    {
        var post = await PostAsync();
        await _service.SubmitAsync(post, new SubmitFeedbackRequest(["useful"], null));

        _fixture.Db.Remove((await _fixture.Posts.GetByIdAsync(post))!);
        await _fixture.Db.SaveChangesAsync();

        (await _service.GetPageAsync(1, 10)).TotalCount.Should().Be(0);
    }
}

/// <summary>Feedback through the real HTTP pipeline: anyone may send it, only the owner may read or delete it, and it is rate limited.</summary>
public class FeedbackPipelineTests : IClassFixture<AdminApiFactory>
{
    private static int _ipCounter = 10;
    private readonly AdminApiFactory _factory;
    private readonly HttpClient _client;

    public FeedbackPipelineTests(AdminApiFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    private Task<HttpResponseMessage> Send(HttpMethod method, string path, string? token = null, object? body = null, string? ip = null)
    {
        var request = new HttpRequestMessage(method, path);
        request.Headers.Add("CF-Connecting-IP", ip ?? $"198.51.100.{Interlocked.Increment(ref _ipCounter) % 250}");
        if (token is not null) request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        if (body is not null) request.Content = JsonContent.Create(body);
        return _client.SendAsync(request);
    }

    private static async Task<JsonElement> Json(HttpResponseMessage response) =>
        JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement.Clone();

    private async Task<string> AdminTokenAsync()
    {
        await _factory.EnsureUsersAsync();
        return (await Json(await Send(HttpMethod.Post, "/api/v1/admin/auth/login", body: new { email = AdminApiFactory.AdminEmail, code = _factory.FreshCode() }))).GetProperty("token").GetString()!;
    }

    private async Task<string> PublishedPostAsync(string token)
    {
        var post = await Json(await Send(HttpMethod.Post, "/api/v1/posts", token,
            new { title = $"Feedback {Guid.NewGuid():N}"[..20], subtitle = (string?)null, contentJson = TestDatabase.Document("Body."), coverImageUrl = (string?)null, tags = Array.Empty<string>(), categoryId = (string?)null }));
        var id = post.GetProperty("id").GetString()!;
        await Send(HttpMethod.Post, $"/api/v1/posts/{id}/status", token, new { status = "Published" });
        return id;
    }

    [Fact]
    public async Task Anyone_can_send_feedback_and_the_owner_reads_and_deletes_it()
    {
        var token = await AdminTokenAsync();
        var post = await PublishedPostAsync(token);

        (await Send(HttpMethod.Post, $"/api/v1/posts/{post}/feedback", body: new { liked = new[] { "useful" }, text = "Great read" })).StatusCode.Should().Be(HttpStatusCode.NoContent);

        var page = await Json(await Send(HttpMethod.Get, "/api/v1/admin/feedback", token));
        var item = page.GetProperty("items").EnumerateArray().First(i => i.GetProperty("postId").GetString() == post);
        item.GetProperty("text").GetString().Should().Be("Great read");
        item.GetProperty("liked").EnumerateArray().Select(l => l.GetString()).Should().Equal("useful");

        (await Send(HttpMethod.Delete, $"/api/v1/admin/feedback/{item.GetProperty("id").GetString()}", token)).StatusCode.Should().Be(HttpStatusCode.NoContent);
    }

    [Fact]
    public async Task Only_the_owner_can_read_or_delete_feedback()
    {
        (await Send(HttpMethod.Get, "/api/v1/admin/feedback")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await Send(HttpMethod.Delete, $"/api/v1/admin/feedback/{Guid.NewGuid()}")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Empty_or_invalid_feedback_is_a_bad_request_and_an_unknown_post_is_not_found()
    {
        var token = await AdminTokenAsync();
        var post = await PublishedPostAsync(token);

        (await Send(HttpMethod.Post, $"/api/v1/posts/{post}/feedback", body: new { liked = Array.Empty<string>(), text = "" })).StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await Send(HttpMethod.Post, $"/api/v1/posts/{post}/feedback", body: new { liked = new[] { "nope" } })).StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await Send(HttpMethod.Post, $"/api/v1/posts/{Guid.NewGuid()}/feedback", body: new { liked = new[] { "useful" } })).StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task One_reader_can_send_only_a_few_an_hour()
    {
        var token = await AdminTokenAsync();
        var post = await PublishedPostAsync(token);
        const string ip = "203.0.113.77";

        var codes = new List<HttpStatusCode>();
        for (var i = 0; i < 7; i++)
            codes.Add((await Send(HttpMethod.Post, $"/api/v1/posts/{post}/feedback", body: new { liked = new[] { "useful" } }, ip: ip)).StatusCode);

        codes.Take(5).Should().OnlyContain(c => c == HttpStatusCode.NoContent);
        codes.Skip(5).Should().OnlyContain(c => c == HttpStatusCode.TooManyRequests);
    }
}
