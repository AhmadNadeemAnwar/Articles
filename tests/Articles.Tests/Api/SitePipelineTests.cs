using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Xunit;

namespace Articles.Tests.Api;

/// <summary>The site's theme and categories through the real HTTP pipeline: who may read them and who may change them.</summary>
public class SitePipelineTests : IClassFixture<AdminApiFactory>
{
    private static int _ipCounter = 150;
    private readonly AdminApiFactory _factory;
    private readonly HttpClient _client;

    public SitePipelineTests(AdminApiFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    private static string NewIp() => $"203.0.113.{Interlocked.Increment(ref _ipCounter)}";

    private Task<HttpResponseMessage> Send(HttpMethod method, string path, string? token = null, object? body = null)
    {
        var request = new HttpRequestMessage(method, path);
        request.Headers.Add("CF-Connecting-IP", NewIp());
        if (token is not null) request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        if (body is not null) request.Content = JsonContent.Create(body);
        return _client.SendAsync(request);
    }

    private static async Task<JsonElement> Json(HttpResponseMessage response) =>
        JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement.Clone();

    private async Task<string> AdminTokenAsync()
    {
        await _factory.EnsureUsersAsync();
        var response = await Send(HttpMethod.Post, "/api/v1/admin/auth/login", body: new { email = AdminApiFactory.AdminEmail, code = _factory.FreshCode() });
        return (await Json(response)).GetProperty("token").GetString()!;
    }

    private async Task<string> ReaderTokenAsync()
    {
        await _factory.EnsureUsersAsync();
        var response = await Send(HttpMethod.Post, "/api/v1/auth/login", body: new { email = AdminApiFactory.ReaderEmail, password = AdminApiFactory.Password });
        return (await Json(response)).GetProperty("token").GetString()!;
    }

    [Fact]
    public async Task Any_visitor_can_read_the_theme_and_categories()
    {
        var response = await Send(HttpMethod.Get, "/api/v1/site");
        var site = await Json(response);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        site.GetProperty("theme").GetString().Should().BeOneOf("blue", "seagreen", "custom");
        site.GetProperty("categories").ValueKind.Should().Be(JsonValueKind.Array);
    }

    [Fact]
    public async Task Only_an_admin_session_can_change_the_theme_or_the_categories()
    {
        var reader = await ReaderTokenAsync();
        var attempts = new (HttpMethod Method, string Path, object? Body)[]
        {
            (HttpMethod.Get, "/api/v1/admin/settings", null),
            (HttpMethod.Put, "/api/v1/admin/settings", new { theme = "seagreen" }),
            (HttpMethod.Get, "/api/v1/admin/categories", null),
            (HttpMethod.Post, "/api/v1/admin/categories", new { name = "Sneaky" }),
            (HttpMethod.Put, $"/api/v1/admin/categories/{Guid.NewGuid()}", new { name = "Sneaky" }),
            (HttpMethod.Delete, $"/api/v1/admin/categories/{Guid.NewGuid()}", null),
        };

        foreach (var (method, path, body) in attempts)
        {
            (await Send(method, path, body: body)).StatusCode.Should().Be(HttpStatusCode.Unauthorized, $"{method} {path} with no session");
            (await Send(method, path, reader, body)).StatusCode.Should().Be(HttpStatusCode.Forbidden, $"{method} {path} as an ordinary account");
        }

        (await Json(await Send(HttpMethod.Get, "/api/v1/site"))).GetProperty("categories").EnumerateArray()
            .Should().NotContain(c => c.GetProperty("name").GetString() == "Sneaky");
    }

    [Fact]
    public async Task The_owner_switches_the_theme_and_every_visitor_gets_it()
    {
        var token = await AdminTokenAsync();

        var saved = await Send(HttpMethod.Put, "/api/v1/admin/settings", token, new { theme = "seagreen" });

        saved.StatusCode.Should().Be(HttpStatusCode.OK);
        (await Json(await Send(HttpMethod.Get, "/api/v1/site"))).GetProperty("theme").GetString().Should().Be("seagreen");

        (await Send(HttpMethod.Put, "/api/v1/admin/settings", token, new { theme = "blue" })).StatusCode.Should().Be(HttpStatusCode.OK);
        (await Json(await Send(HttpMethod.Get, "/api/v1/site"))).GetProperty("theme").GetString().Should().Be("blue");
    }

    [Fact]
    public async Task The_owner_sets_custom_colours_and_every_visitor_gets_them()
    {
        var token = await AdminTokenAsync();

        var saved = await Send(HttpMethod.Put, "/api/v1/admin/settings", token, new { theme = "custom", main = "#7A1F5C", background = "#fff8f0", accent = "#B35A00" });

        saved.StatusCode.Should().Be(HttpStatusCode.OK);
        (await Json(saved)).GetProperty("colors").GetProperty("main").GetString().Should().Be("#7a1f5c");
        var site = await Json(await Send(HttpMethod.Get, "/api/v1/site"));
        site.GetProperty("theme").GetString().Should().Be("custom");
        site.GetProperty("colors").GetProperty("main").GetString().Should().Be("#7a1f5c");
        site.GetProperty("colors").GetProperty("background").GetString().Should().Be("#fff8f0");
        site.GetProperty("colors").GetProperty("accent").GetString().Should().Be("#b35a00");

        (await Send(HttpMethod.Put, "/api/v1/admin/settings", token, new { theme = "blue" })).StatusCode.Should().Be(HttpStatusCode.OK);
        (await Json(await Send(HttpMethod.Get, "/api/v1/site"))).GetProperty("colors").ValueKind.Should().Be(JsonValueKind.Null);
    }

    [Theory]
    [InlineData("red", "#ffffff")]
    [InlineData("#17694a", "#808080")]
    [InlineData("#17694a", "#fff;}body{display:none")]
    public async Task Custom_colours_that_are_not_usable_are_refused(string main, string background)
    {
        var token = await AdminTokenAsync();

        (await Send(HttpMethod.Put, "/api/v1/admin/settings", token, new { theme = "custom", main, background })).StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await Json(await Send(HttpMethod.Get, "/api/v1/site"))).GetProperty("theme").GetString().Should().NotBe("custom");
    }

    [Theory]
    [InlineData("neon")]
    [InlineData("")]
    public async Task A_theme_the_site_does_not_have_is_refused(string theme)
    {
        var token = await AdminTokenAsync();

        (await Send(HttpMethod.Put, "/api/v1/admin/settings", token, new { theme })).StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task A_category_can_be_made_put_on_a_post_browsed_renamed_and_removed()
    {
        var token = await AdminTokenAsync();
        var name = $"Shelf {Guid.NewGuid():N}"[..20];

        var created = await Json(await Send(HttpMethod.Post, "/api/v1/admin/categories", token, new { name }));
        var id = created.GetProperty("id").GetString()!;
        var slug = created.GetProperty("slug").GetString()!;

        var post = await Json(await Send(HttpMethod.Post, "/api/v1/posts", token,
            new { title = "Shelved post", subtitle = (string?)null, contentJson = TestDatabase.Document("Body."), coverImageUrl = (string?)null, tags = Array.Empty<string>(), categoryId = id }));
        var postId = post.GetProperty("id").GetString();
        await Send(HttpMethod.Post, $"/api/v1/posts/{postId}/status", token, new { status = "Published" });

        // Readers see the category, with its count, and can browse by it.
        var listed = (await Json(await Send(HttpMethod.Get, "/api/v1/site"))).GetProperty("categories").EnumerateArray().Single(c => c.GetProperty("slug").GetString() == slug);
        listed.GetProperty("postCount").GetInt32().Should().Be(1);
        var browsed = await Json(await Send(HttpMethod.Get, $"/api/v1/posts?category={slug}"));
        browsed.GetProperty("totalCount").GetInt32().Should().Be(1);
        browsed.GetProperty("items")[0].GetProperty("category").GetProperty("name").GetString().Should().Be(name);

        // Renaming keeps the address.
        var renamed = await Json(await Send(HttpMethod.Put, $"/api/v1/admin/categories/{id}", token, new { name = name + " 2" }));
        renamed.GetProperty("slug").GetString().Should().Be(slug);

        // Removing it keeps the post.
        (await Send(HttpMethod.Delete, $"/api/v1/admin/categories/{id}", token)).StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await Json(await Send(HttpMethod.Get, $"/api/v1/posts?category={slug}"))).GetProperty("totalCount").GetInt32().Should().Be(0);
        var edit = await Json(await Send(HttpMethod.Get, $"/api/v1/posts/{postId}/edit", token));
        edit.GetProperty("category").ValueKind.Should().Be(JsonValueKind.Null);
        edit.GetProperty("status").GetString().Should().Be("Published");
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("This category name is far too long to be a tab on the home page")]
    public async Task A_category_needs_a_short_name(string name)
    {
        var token = await AdminTokenAsync();

        (await Send(HttpMethod.Post, "/api/v1/admin/categories", token, new { name })).StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task A_post_cannot_be_put_in_a_category_that_does_not_exist()
    {
        var token = await AdminTokenAsync();

        var response = await Send(HttpMethod.Post, "/api/v1/posts", token,
            new { title = "Nowhere", subtitle = (string?)null, contentJson = TestDatabase.Document("Body."), coverImageUrl = (string?)null, tags = Array.Empty<string>(), categoryId = Guid.NewGuid() });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task A_newly_published_post_gets_an_address_ending_in_a_short_code()
    {
        var token = await AdminTokenAsync();
        var post = await Json(await Send(HttpMethod.Post, "/api/v1/posts", token,
            new { title = "Today for Tomorrow", subtitle = (string?)null, contentJson = TestDatabase.Document("Body."), coverImageUrl = (string?)null, tags = Array.Empty<string>() }));

        var published = await Json(await Send(HttpMethod.Post, $"/api/v1/posts/{post.GetProperty("id").GetString()}/status", token, new { status = "Published" }));
        var slug = published.GetProperty("slug").GetString()!;

        slug.Should().MatchRegex("^today-for-tomorrow-[23456789a-hjkmnp-z]{5}$");
        (await Send(HttpMethod.Get, $"/api/v1/posts/{slug}")).StatusCode.Should().Be(HttpStatusCode.OK);
    }


    [Fact]
    public async Task Only_an_admin_session_can_read_or_change_the_note()
    {
        (await Send(HttpMethod.Get, "/api/v1/admin/global-note")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await Send(HttpMethod.Put, "/api/v1/admin/global-note", body: new { text = "x" })).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await Send(HttpMethod.Put, "/api/v1/admin/global-note", await ReaderTokenAsync(), new { text = "x" })).StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task The_owner_saves_a_note_and_every_visitor_gets_it_with_the_site()
    {
        var token = await AdminTokenAsync();

        var saved = await Send(HttpMethod.Put, "/api/v1/admin/global-note", token, new { text = "Thanks for reading." });

        saved.StatusCode.Should().Be(HttpStatusCode.OK);
        (await Json(await Send(HttpMethod.Get, "/api/v1/admin/global-note", token))).GetProperty("text").GetString().Should().Be("Thanks for reading.");
        (await Json(await Send(HttpMethod.Get, "/api/v1/site"))).GetProperty("note").GetString().Should().Be("Thanks for reading.");

        await Send(HttpMethod.Put, "/api/v1/admin/global-note", token, new { text = "" });
        (await Json(await Send(HttpMethod.Get, "/api/v1/site"))).GetProperty("note").ValueKind.Should().Be(JsonValueKind.Null);
    }

    [Fact]
    public async Task A_note_that_is_too_long_is_a_plain_bad_request()
    {
        var response = await Send(HttpMethod.Put, "/api/v1/admin/global-note", await AdminTokenAsync(), new { text = new string('x', 1001) });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }
}
