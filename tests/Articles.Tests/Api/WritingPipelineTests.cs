using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Articles.Application.Images;
using Articles.Tests.Images;
using Xunit;

namespace Articles.Tests.Api;

/// <summary>
/// Writing a post through the real HTTP pipeline, in Production configuration: uploading pictures,
/// saving a body made of sections, and moving a post between its three states.
/// </summary>
public class WritingPipelineTests : IClassFixture<AdminApiFactory>
{
    private static int _ipCounter = 120;
    private readonly AdminApiFactory _factory;
    private readonly HttpClient _client;

    public WritingPipelineTests(AdminApiFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    private static string NewIp() => $"198.51.100.{Interlocked.Increment(ref _ipCounter)}";

    private Task<HttpResponseMessage> Send(HttpMethod method, string path, string? token = null, object? body = null, HttpContent? content = null)
    {
        var request = new HttpRequestMessage(method, path);
        request.Headers.Add("CF-Connecting-IP", NewIp());
        if (token is not null) request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        if (body is not null) request.Content = JsonContent.Create(body);
        if (content is not null) request.Content = content;
        return _client.SendAsync(request);
    }

    private async Task<string> AdminTokenAsync()
    {
        await _factory.EnsureUsersAsync();
        var response = await Send(HttpMethod.Post, "/api/v1/admin/auth/login",
            body: new { email = AdminApiFactory.AdminEmail, code = _factory.FreshCode() });
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        return (await Json(response)).GetProperty("token").GetString()!;
    }

    private async Task<string> ReaderTokenAsync()
    {
        await _factory.EnsureUsersAsync();
        var response = await Send(HttpMethod.Post, "/api/v1/auth/login",
            body: new { email = AdminApiFactory.ReaderEmail, password = AdminApiFactory.Password });
        return (await Json(response)).GetProperty("token").GetString()!;
    }

    private static async Task<JsonElement> Json(HttpResponseMessage response) =>
        JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement.Clone();

    private static MultipartFormDataContent Upload(byte[] data, string fileName = "photo.png", string contentType = "image/png")
    {
        var file = new ByteArrayContent(data);
        file.Headers.ContentType = new MediaTypeHeaderValue(contentType);
        return new MultipartFormDataContent { { file, "file", fileName } };
    }

    private async Task<JsonElement> UploadedAsync(string token, byte[]? data = null)
    {
        var response = await Send(HttpMethod.Post, "/api/v1/images", token, content: Upload(data ?? SampleImages.Png(400)));
        response.StatusCode.Should().Be(HttpStatusCode.Created);
        return await Json(response);
    }

    private async Task<string> DraftAsync(string token, string title, string contentJson, string? cover = null)
    {
        var response = await Send(HttpMethod.Post, "/api/v1/posts", token,
            new { title, subtitle = (string?)null, contentJson, coverImageUrl = cover, tags = new[] { "testing" } });
        response.StatusCode.Should().Be(HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
        return (await Json(response)).GetProperty("id").GetString()!;
    }

    private static string SectionsWith(string imageId) =>
        $$$"""
        {"type":"sections","content":[
          {"type":"textSection","content":[{"type":"paragraph","content":[{"type":"text","text":"An opening paragraph."}]}]},
          {"type":"imageSection","attrs":{"imageId":"{{{imageId}}}","alt":"A chart","caption":"Figure 1"}},
          {"type":"referencesSection","attrs":{"items":[{"title":"The source","url":"https://example.com/source"}]}}
        ]}
        """;

    // ---- Pictures -----------------------------------------------------------------------------

    [Fact]
    public async Task Uploading_a_picture_needs_a_signed_in_writer()
    {
        var response = await Send(HttpMethod.Post, "/api/v1/images", content: Upload(SampleImages.Png()));

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task An_uploaded_picture_is_served_to_anyone_and_may_be_kept_by_browsers()
    {
        var token = await AdminTokenAsync();
        var data = SampleImages.Png(400);
        var uploaded = await UploadedAsync(token, data);

        var response = await Send(HttpMethod.Get, uploaded.GetProperty("path").GetString()!);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Content.Headers.ContentType!.MediaType.Should().Be("image/png");
        (await response.Content.ReadAsByteArrayAsync()).Should().Equal(data);
        response.Headers.CacheControl!.ToString().Should().Contain("immutable").And.Contain("public").And.NotContain("no-store");
        response.Headers.GetValues("X-Content-Type-Options").Should().ContainSingle("nosniff");
        response.Headers.GetValues("Cross-Origin-Resource-Policy").Should().ContainSingle("cross-origin");
    }

    [Fact]
    public async Task The_type_is_decided_by_the_file_not_by_what_the_browser_claims()
    {
        var token = await AdminTokenAsync();
        var script = System.Text.Encoding.ASCII.GetBytes("<svg xmlns='http://www.w3.org/2000/svg'><script>alert(1)</script></svg>");

        var response = await Send(HttpMethod.Post, "/api/v1/images", token, content: Upload(script, "innocent.png", "image/png"));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await response.Content.ReadAsStringAsync()).Should().Contain("JPEG, PNG and WebP");
    }

    [Fact]
    public async Task A_picture_over_the_limit_is_refused()
    {
        var token = await AdminTokenAsync();

        var response = await Send(HttpMethod.Post, "/api/v1/images", token, content: Upload(SampleImages.Png(ImageService.MaxBytes + 1)));

        ((int)response.StatusCode).Should().BeOneOf(400, 413);
    }

    [Fact]
    public async Task A_request_with_no_file_is_refused()
    {
        var token = await AdminTokenAsync();

        var response = await Send(HttpMethod.Post, "/api/v1/images", token, content: new MultipartFormDataContent { { new StringContent("x"), "note" } });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task An_unknown_picture_is_not_found_and_is_not_cacheable()
    {
        var response = await Send(HttpMethod.Get, $"/api/v1/images/{Guid.NewGuid()}");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        response.Headers.CacheControl!.ToString().Should().Contain("no-store");
    }

    // ---- A post made of sections ---------------------------------------------------------------

    [Fact]
    public async Task A_post_made_of_sections_can_be_saved_published_and_read_by_anyone()
    {
        var token = await AdminTokenAsync();
        var image = await UploadedAsync(token);
        var id = await DraftAsync(token, "Sections end to end", SectionsWith(image.GetProperty("id").GetString()!), image.GetProperty("path").GetString());

        var published = await Json(await Send(HttpMethod.Post, $"/api/v1/posts/{id}/status", token, new { status = "Published" }));
        var read = await Send(HttpMethod.Get, $"/api/v1/posts/{published.GetProperty("slug").GetString()}");

        read.StatusCode.Should().Be(HttpStatusCode.OK);
        var post = await Json(read);
        post.GetProperty("contentJson").GetString().Should().Contain("imageSection").And.Contain("referencesSection");
        post.GetProperty("coverImageUrl").GetString().Should().Be(image.GetProperty("path").GetString());
    }

    [Fact]
    public async Task A_post_with_sections_is_found_by_words_in_its_caption_and_its_sources()
    {
        var token = await AdminTokenAsync();
        var image = await UploadedAsync(token);
        var body = SectionsWith(image.GetProperty("id").GetString()!).Replace("Figure 1", "Zanzibar harbour").Replace("The source", "Quokka handbook");
        var id = await DraftAsync(token, "Findable by caption", body);
        await Send(HttpMethod.Post, $"/api/v1/posts/{id}/status", token, new { status = "Published" });

        foreach (var word in new[] { "Zanzibar", "Quokka" })
        {
            var results = await Json(await Send(HttpMethod.Get, $"/api/v1/posts?q={word}"));
            results.GetProperty("totalCount").GetInt32().Should().Be(1, word);
        }
    }

    [Theory]
    [InlineData("""{"type":"sections","content":[{"type":"scriptSection"}]}""")]
    [InlineData("""{"type":"sections","content":[{"type":"imageSection","attrs":{"imageId":"https://evil.example/x.png"}}]}""")]
    [InlineData("""{"type":"sections","content":[{"type":"referencesSection","attrs":{"items":[{"title":"Click me","url":"javascript:alert(1)"}]}}]}""")]
    [InlineData("""{"type":"mystery"}""")]
    public async Task A_malformed_or_unsafe_body_is_refused(string contentJson)
    {
        var token = await AdminTokenAsync();

        var response = await Send(HttpMethod.Post, "/api/v1/posts", token,
            new { title = "Should not save", subtitle = (string?)null, contentJson, coverImageUrl = (string?)null, tags = Array.Empty<string>() });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Theory]
    [InlineData("/api/v1/images/not-a-guid")]
    [InlineData("/api/v1/admin/stats")]
    [InlineData("//evil.example/x.png")]
    [InlineData("http://example.com/insecure.png")]
    [InlineData("javascript:alert(1)")]
    public async Task A_cover_must_be_an_uploaded_picture_or_an_https_link(string cover)
    {
        var token = await AdminTokenAsync();

        var response = await Send(HttpMethod.Post, "/api/v1/posts", token,
            new { title = "Bad cover", subtitle = (string?)null, contentJson = TestDatabase.Document("Body."), coverImageUrl = cover, tags = Array.Empty<string>() });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    // ---- The three states ----------------------------------------------------------------------

    [Fact]
    public async Task A_post_moves_between_draft_published_and_inactive_and_keeps_its_address()
    {
        var token = await AdminTokenAsync();
        var id = await DraftAsync(token, "Three states", TestDatabase.Document("Body."));

        async Task<JsonElement> Set(string status) =>
            await Json(await Send(HttpMethod.Post, $"/api/v1/posts/{id}/status", token, new { status }));

        var published = await Set("Published");
        var slug = published.GetProperty("slug").GetString();
        published.GetProperty("status").GetString().Should().Be("Published");

        (await Set("Inactive")).GetProperty("status").GetString().Should().Be("Inactive");
        (await Send(HttpMethod.Get, $"/api/v1/posts/{slug}")).StatusCode.Should().Be(HttpStatusCode.NotFound, "an inactive post is hidden from readers");

        (await Set("Draft")).GetProperty("status").GetString().Should().Be("Draft");
        (await Send(HttpMethod.Get, $"/api/v1/posts/{slug}")).StatusCode.Should().Be(HttpStatusCode.NotFound);

        var again = await Set("Published");
        again.GetProperty("slug").GetString().Should().Be(slug, "publishing again must restore the same link");
        (await Send(HttpMethod.Get, $"/api/v1/posts/{slug}")).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task An_inactive_post_disappears_from_the_public_list_and_from_search()
    {
        var token = await AdminTokenAsync();
        var id = await DraftAsync(token, "Xylophone retirement", TestDatabase.Document("Body."));
        await Send(HttpMethod.Post, $"/api/v1/posts/{id}/status", token, new { status = "Published" });
        (await Json(await Send(HttpMethod.Get, "/api/v1/posts?q=Xylophone"))).GetProperty("totalCount").GetInt32().Should().Be(1);

        await Send(HttpMethod.Post, $"/api/v1/posts/{id}/status", token, new { status = "Inactive" });

        (await Json(await Send(HttpMethod.Get, "/api/v1/posts?q=Xylophone"))).GetProperty("totalCount").GetInt32().Should().Be(0);
    }

    [Theory]
    [InlineData("Unlisted")]
    [InlineData("Archived")]
    [InlineData("1")]
    [InlineData("")]
    public async Task Only_the_three_named_states_are_accepted(string status)
    {
        var token = await AdminTokenAsync();
        var id = await DraftAsync(token, "Status guard", TestDatabase.Document("Body."));

        var response = await Send(HttpMethod.Post, $"/api/v1/posts/{id}/status", token, new { status });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Changing_to_the_state_a_post_is_already_in_is_refused()
    {
        var token = await AdminTokenAsync();
        var id = await DraftAsync(token, "Already a draft", TestDatabase.Document("Body."));

        (await Send(HttpMethod.Post, $"/api/v1/posts/{id}/status", token, new { status = "Draft" })).StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Someone_else_cannot_change_the_state_of_your_post()
    {
        var token = await AdminTokenAsync();
        var reader = await ReaderTokenAsync();
        var id = await DraftAsync(token, "Not yours", TestDatabase.Document("Body."));

        (await Send(HttpMethod.Post, $"/api/v1/posts/{id}/status", reader, new { status = "Published" })).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await Send(HttpMethod.Post, $"/api/v1/posts/{id}/status", body: new { status = "Published" })).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task The_admin_route_can_put_a_post_back_up_and_ordinary_accounts_cannot_use_it()
    {
        var token = await AdminTokenAsync();
        var reader = await ReaderTokenAsync();
        var id = await DraftAsync(token, "Back up again", TestDatabase.Document("Body."));
        await Send(HttpMethod.Post, $"/api/v1/posts/{id}/status", token, new { status = "Published" });
        await Send(HttpMethod.Post, $"/api/v1/admin/posts/{id}/unpublish", token);

        (await Send(HttpMethod.Post, $"/api/v1/admin/posts/{id}/status", reader, new { status = "Published" })).StatusCode.Should().Be(HttpStatusCode.Forbidden);

        var listed = await Json(await Send(HttpMethod.Get, "/api/v1/admin/posts?status=Inactive", token));
        listed.GetProperty("items").EnumerateArray().Select(p => p.GetProperty("id").GetString()).Should().Contain(id);

        (await Send(HttpMethod.Post, $"/api/v1/admin/posts/{id}/status", token, new { status = "Published" })).StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await Json(await Send(HttpMethod.Get, "/api/v1/admin/posts?status=Published", token)))
            .GetProperty("items").EnumerateArray().Select(p => p.GetProperty("id").GetString()).Should().Contain(id);
    }
}
