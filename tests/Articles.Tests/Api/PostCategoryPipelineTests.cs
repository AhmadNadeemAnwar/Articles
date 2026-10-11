using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Xunit;

namespace Articles.Tests.Api;

/// <summary>Moving posts between categories through the real HTTP pipeline: who may do it, and what the posts list shows.</summary>
public class PostCategoryPipelineTests : IClassFixture<AdminApiFactory>
{
    private static int _ipCounter = 40;
    private readonly AdminApiFactory _factory;
    private readonly HttpClient _client;

    public PostCategoryPipelineTests(AdminApiFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    private static string NewIp() => $"198.18.0.{Interlocked.Increment(ref _ipCounter) % 250}";

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

    private async Task<string> NewCategoryAsync(string token)
    {
        var name = $"Shelf {Guid.NewGuid():N}"[..20];
        return (await Json(await Send(HttpMethod.Post, "/api/v1/admin/categories", token, new { name }))).GetProperty("id").GetString()!;
    }

    private async Task<string> NewPublishedPostAsync(string token, string title)
    {
        var post = await Json(await Send(HttpMethod.Post, "/api/v1/posts", token,
            new { title, subtitle = (string?)null, contentJson = TestDatabase.Document("Body."), coverImageUrl = (string?)null, tags = Array.Empty<string>(), categoryId = (string?)null }));
        var id = post.GetProperty("id").GetString()!;
        await Send(HttpMethod.Post, $"/api/v1/posts/{id}/status", token, new { status = "Published" });
        return id;
    }

    private async Task<List<string>> ListedAsync(string token, string query)
    {
        var page = await Json(await Send(HttpMethod.Get, $"/api/v1/admin/posts?{query}", token));
        return page.GetProperty("items").EnumerateArray().Select(p => p.GetProperty("id").GetString()!).ToList();
    }

    [Fact]
    public async Task Only_an_admin_session_can_move_a_post()
    {
        var path = $"/api/v1/admin/posts/{Guid.NewGuid()}/category";

        (await Send(HttpMethod.Post, path, body: new { categoryId = (string?)null })).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await Send(HttpMethod.Post, path, await ReaderTokenAsync(), new { categoryId = (string?)null })).StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task An_admin_moves_a_post_into_a_category_sees_it_there_and_takes_it_out_again()
    {
        var token = await AdminTokenAsync();
        var category = await NewCategoryAsync(token);
        var post = await NewPublishedPostAsync(token, "To be moved");

        (await Send(HttpMethod.Post, $"/api/v1/admin/posts/{post}/category", token, new { categoryId = category })).StatusCode.Should().Be(HttpStatusCode.NoContent);

        var row = (await Json(await Send(HttpMethod.Get, $"/api/v1/admin/posts?category={category}", token))).GetProperty("items").EnumerateArray().Single();
        row.GetProperty("id").GetString().Should().Be(post);
        row.GetProperty("categoryId").GetString().Should().Be(category);
        row.GetProperty("status").GetString().Should().Be("Published");
        (await ListedAsync(token, "category=none")).Should().NotContain(post);

        (await Send(HttpMethod.Post, $"/api/v1/admin/posts/{post}/category", token, new { categoryId = (string?)null })).StatusCode.Should().Be(HttpStatusCode.NoContent);

        (await ListedAsync(token, $"category={category}")).Should().BeEmpty();
        (await ListedAsync(token, "category=none")).Should().Contain(post);
    }

    [Fact]
    public async Task Readers_see_a_moved_post_under_its_category()
    {
        var token = await AdminTokenAsync();
        var created = await Json(await Send(HttpMethod.Post, "/api/v1/admin/categories", token, new { name = $"Readers {Guid.NewGuid():N}"[..20] }));
        var post = await NewPublishedPostAsync(token, "Seen by readers");

        await Send(HttpMethod.Post, $"/api/v1/admin/posts/{post}/category", token, new { categoryId = created.GetProperty("id").GetString() });

        var browsed = await Json(await Send(HttpMethod.Get, $"/api/v1/posts?category={created.GetProperty("slug").GetString()}"));
        browsed.GetProperty("items").EnumerateArray().Select(p => p.GetProperty("id").GetString()).Should().Equal(post);
    }

    [Fact]
    public async Task A_category_that_does_not_exist_is_a_bad_request_and_the_post_is_unchanged()
    {
        var token = await AdminTokenAsync();
        var post = await NewPublishedPostAsync(token, "Stays put");

        var response = await Send(HttpMethod.Post, $"/api/v1/admin/posts/{post}/category", token, new { categoryId = Guid.NewGuid() });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await ListedAsync(token, "category=none")).Should().Contain(post);
    }

    [Fact]
    public async Task A_post_that_does_not_exist_is_not_found()
    {
        var response = await Send(HttpMethod.Post, $"/api/v1/admin/posts/{Guid.NewGuid()}/category", await AdminTokenAsync(), new { categoryId = (string?)null });

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Theory]
    [InlineData("nonsense")]
    [InlineData("1")]
    public async Task A_category_filter_that_is_not_none_or_an_id_is_a_bad_request(string filter)
    {
        (await Send(HttpMethod.Get, $"/api/v1/admin/posts?category={filter}", await AdminTokenAsync())).StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Moving_a_post_is_written_in_the_activity_history()
    {
        var token = await AdminTokenAsync();
        var category = await NewCategoryAsync(token);
        var post = await NewPublishedPostAsync(token, $"Logged {Guid.NewGuid():N}"[..20]);

        await Send(HttpMethod.Post, $"/api/v1/admin/posts/{post}/category", token, new { categoryId = category });

        var history = await Json(await Send(HttpMethod.Get, "/api/v1/admin/activity?pageSize=50", token));
        history.GetProperty("items").EnumerateArray().Select(e => e.GetProperty("action").GetString()).Should().Contain("Moved post to a category");
    }
}
