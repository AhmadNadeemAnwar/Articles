using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Articles.Tests.Images;
using Xunit;

namespace Articles.Tests.Api;

/// <summary>The owner's tools through the real HTTP pipeline: activity history, export, profile and saved revisions.</summary>
public class OwnerToolsPipelineTests : IClassFixture<AdminApiFactory>
{
    private static int _ipCounter = 60;
    private readonly AdminApiFactory _factory;
    private readonly HttpClient _client;

    public OwnerToolsPipelineTests(AdminApiFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    private static string NewIp() => $"203.0.113.{Interlocked.Increment(ref _ipCounter)}";

    private Task<HttpResponseMessage> Send(HttpMethod method, string path, string? token = null, object? body = null, HttpContent? content = null)
    {
        var request = new HttpRequestMessage(method, path);
        request.Headers.Add("CF-Connecting-IP", NewIp());
        if (token is not null) request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        if (body is not null) request.Content = JsonContent.Create(body);
        if (content is not null) request.Content = content;
        return _client.SendAsync(request);
    }

    private static async Task<JsonElement> Json(HttpResponseMessage response) =>
        JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement.Clone();

    private async Task<string> AdminTokenAsync()
    {
        await _factory.EnsureUsersAsync();
        var response = await Send(HttpMethod.Post, "/api/v1/admin/auth/login", body: new { email = AdminApiFactory.AdminEmail, code = _factory.FreshCode() });
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        return (await Json(response)).GetProperty("token").GetString()!;
    }

    private async Task<string> ReaderTokenAsync()
    {
        await _factory.EnsureUsersAsync();
        var response = await Send(HttpMethod.Post, "/api/v1/auth/login", body: new { email = AdminApiFactory.ReaderEmail, password = AdminApiFactory.Password });
        return (await Json(response)).GetProperty("token").GetString()!;
    }

    private async Task<string> DraftAsync(string token, string title, string body = "Body.")
    {
        var created = await Send(HttpMethod.Post, "/api/v1/posts", token,
            new { title, subtitle = (string?)null, contentJson = TestDatabase.Document(body), coverImageUrl = (string?)null, tags = new[] { "export" } });
        return (await Json(created)).GetProperty("id").GetString()!;
    }

    private async Task<List<JsonElement>> HistoryAsync(string token) =>
        (await Json(await Send(HttpMethod.Get, "/api/v1/admin/activity?pageSize=100", token))).GetProperty("items").EnumerateArray().ToList();

    // ---- Activity history ---------------------------------------------------------------------

    [Fact]
    public async Task Activity_and_export_need_an_admin_session()
    {
        var reader = await ReaderTokenAsync();

        foreach (var path in new[] { "/api/v1/admin/activity", "/api/v1/admin/export" })
        {
            (await Send(HttpMethod.Get, path)).StatusCode.Should().Be(HttpStatusCode.Unauthorized, path);
            (await Send(HttpMethod.Get, path, reader)).StatusCode.Should().Be(HttpStatusCode.Forbidden, path);
        }
    }

    [Fact]
    public async Task Signing_in_and_changing_a_posts_status_both_appear_in_the_history()
    {
        var token = await AdminTokenAsync();
        var id = await DraftAsync(token, "Tracked from start to finish");

        await Send(HttpMethod.Post, $"/api/v1/admin/posts/{id}/status", token, new { status = "Published" });
        await Send(HttpMethod.Post, $"/api/v1/admin/posts/{id}/status", token, new { status = "Inactive" });

        var history = await HistoryAsync(token);
        var subjects = history.Where(h => h.GetProperty("action").GetString() == "Changed post status").Select(h => h.GetProperty("subject").GetString()).ToList();

        subjects.Should().Contain("Tracked from start to finish: Draft to Published");
        subjects.Should().Contain("Tracked from start to finish: Published to Not active");
        history.Should().Contain(h => h.GetProperty("action").GetString() == "Signed in" && h.GetProperty("actor").GetString() == AdminApiFactory.AdminEmail);
    }

    [Fact]
    public async Task A_failed_attempt_on_the_admin_address_is_recorded_but_guesses_at_other_addresses_are_not()
    {
        var token = await AdminTokenAsync();

        await Send(HttpMethod.Post, "/api/v1/admin/auth/login", body: new { email = AdminApiFactory.AdminEmail, code = "000000" });
        await Send(HttpMethod.Post, "/api/v1/admin/auth/login", body: new { email = "stranger@example.com", code = "000000" });

        var history = await HistoryAsync(token);
        history.Should().Contain(h => h.GetProperty("action").GetString() == "Failed sign-in attempt" && h.GetProperty("actor").GetString() == AdminApiFactory.AdminEmail);
        history.Should().NotContain(h => h.GetProperty("actor").GetString() == "stranger@example.com");
    }

    // ---- Export -------------------------------------------------------------------------------

    [Fact]
    public async Task Export_returns_drafts_as_well_as_published_posts_with_their_bodies()
    {
        var token = await AdminTokenAsync();
        var draft = await DraftAsync(token, "Exported while still a draft", "The unfinished body.");
        var live = await DraftAsync(token, "Exported while live", "The finished body.");
        await Send(HttpMethod.Post, $"/api/v1/posts/{live}/status", token, new { status = "Published" });

        var response = await Send(HttpMethod.Get, "/api/v1/admin/export", token);
        var export = await Json(response);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        export.GetProperty("formatVersion").GetInt32().Should().Be(1);
        var posts = export.GetProperty("posts").EnumerateArray().ToDictionary(p => p.GetProperty("id").GetString()!);
        export.GetProperty("postCount").GetInt32().Should().Be(posts.Count);
        posts[draft].GetProperty("status").GetString().Should().Be("Draft");
        posts[draft].GetProperty("plainText").GetString().Should().Be("The unfinished body.");
        posts[live].GetProperty("status").GetString().Should().Be("Published");
        posts[live].GetProperty("contentJson").GetString().Should().Contain("The finished body.");
        posts[live].GetProperty("tags").EnumerateArray().Select(t => t.GetString()).Should().Contain("export");
    }

    // ---- Profile ------------------------------------------------------------------------------

    [Fact]
    public async Task The_owner_can_set_a_bio_and_an_uploaded_photo_and_readers_see_them()
    {
        var token = await AdminTokenAsync();
        var upload = new MultipartFormDataContent();
        var file = new ByteArrayContent(SampleImages.Png(300));
        file.Headers.ContentType = new MediaTypeHeaderValue("image/png");
        upload.Add(file, "file", "me.png");
        var photo = (await Json(await Send(HttpMethod.Post, "/api/v1/images", token, content: upload))).GetProperty("path").GetString();

        var saved = await Send(HttpMethod.Put, "/api/v1/users/me", token,
            new { displayName = "The Owner", bio = "Writes about software.", avatarUrl = photo, websiteUrl = "https://example.com" });
        var profile = await Json(await Send(HttpMethod.Get, "/api/v1/users/the-owner"));

        saved.StatusCode.Should().Be(HttpStatusCode.OK, await saved.Content.ReadAsStringAsync());
        profile.GetProperty("bio").GetString().Should().Be("Writes about software.");
        profile.GetProperty("avatarUrl").GetString().Should().Be(photo);
    }

    [Theory]
    [InlineData("/api/v1/admin/stats")]
    [InlineData("javascript:alert(1)")]
    [InlineData("http://example.com/insecure.png")]
    public async Task A_photo_must_be_an_uploaded_picture_or_an_https_link(string avatarUrl)
    {
        var token = await AdminTokenAsync();

        var response = await Send(HttpMethod.Put, "/api/v1/users/me", token,
            new { displayName = "The Owner", bio = (string?)null, avatarUrl, websiteUrl = (string?)null });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    // ---- Saved revisions ----------------------------------------------------------------------

    [Fact]
    public async Task An_earlier_version_can_be_listed_and_read_back_by_its_author_only()
    {
        var token = await AdminTokenAsync();
        var reader = await ReaderTokenAsync();
        var id = await DraftAsync(token, "Revised", "The original words.");
        await Send(HttpMethod.Put, $"/api/v1/posts/{id}", token,
            new { title = "Revised", subtitle = (string?)null, contentJson = TestDatabase.Document("The new words."), coverImageUrl = (string?)null, tags = Array.Empty<string>() });

        var list = await Json(await Send(HttpMethod.Get, $"/api/v1/posts/{id}/revisions", token));
        var revisionId = list[0].GetProperty("id").GetString();
        var revision = await Json(await Send(HttpMethod.Get, $"/api/v1/posts/{id}/revisions/{revisionId}", token));

        list.GetArrayLength().Should().Be(1);
        revision.GetProperty("contentJson").GetString().Should().Contain("The original words.");
        (await Send(HttpMethod.Get, $"/api/v1/posts/{id}/revisions/{revisionId}", reader)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await Send(HttpMethod.Get, $"/api/v1/posts/{id}/revisions/{revisionId}")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await Send(HttpMethod.Get, $"/api/v1/posts/{id}/revisions/{Guid.NewGuid()}", token)).StatusCode.Should().Be(HttpStatusCode.NotFound);
    }
}
