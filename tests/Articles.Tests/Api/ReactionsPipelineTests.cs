using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Xunit;

namespace Articles.Tests.Api;

/// <summary>Visitors with no account reacting to posts and being counted as readers, through the real HTTP pipeline.</summary>
public class ReactionsPipelineTests : IClassFixture<AdminApiFactory>
{
    private const string Browser = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) Chrome/141.0.0.0 Safari/537.36";

    private static int _ipCounter = 10;
    private readonly AdminApiFactory _factory;
    private readonly HttpClient _client;

    public ReactionsPipelineTests(AdminApiFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    private static string NewIp() => $"203.0.113.{Interlocked.Increment(ref _ipCounter)}";

    private Task<HttpResponseMessage> Send(HttpMethod method, string path, string? token = null, object? body = null,
        string? visitor = null, string? userAgent = Browser, string? origin = null, string? ip = null)
    {
        var request = new HttpRequestMessage(method, path);
        request.Headers.Add("CF-Connecting-IP", ip ?? NewIp());
        if (token is not null) request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        if (visitor is not null) request.Headers.Add("X-Visitor-Id", visitor);
        if (userAgent is not null) request.Headers.TryAddWithoutValidation("User-Agent", userAgent);
        if (origin is not null) request.Headers.Add("Origin", origin);
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

    private async Task<(string Id, string Slug, string Token)> PublishedAsync(string title)
    {
        var token = await AdminTokenAsync();
        var created = await Json(await Send(HttpMethod.Post, "/api/v1/posts", token,
            new { title, subtitle = (string?)null, contentJson = TestDatabase.Document("Body."), coverImageUrl = (string?)null, tags = Array.Empty<string>() }));
        var id = created.GetProperty("id").GetString()!;
        var published = await Json(await Send(HttpMethod.Post, $"/api/v1/posts/{id}/status", token, new { status = "Published" }));
        return (id, published.GetProperty("slug").GetString()!, token);
    }

    private static string Visitor() => Guid.NewGuid().ToString();

    // ---- Reactions ----------------------------------------------------------------------------

    [Fact]
    public async Task A_visitor_with_no_account_can_clap_and_undo()
    {
        var (id, _, _) = await PublishedAsync("Clap me");
        var visitor = Visitor();

        var on = await Send(HttpMethod.Post, $"/api/v1/posts/{id}/reactions/clap", visitor: visitor);
        var off = await Send(HttpMethod.Post, $"/api/v1/posts/{id}/reactions/clap", visitor: visitor);

        on.StatusCode.Should().Be(HttpStatusCode.OK);
        (await Json(on)).GetProperty("clapCount").GetInt32().Should().Be(1);
        (await Json(on)).GetProperty("clapped").GetBoolean().Should().BeTrue();
        (await Json(off)).GetProperty("clapCount").GetInt32().Should().Be(0);
        (await Json(off)).GetProperty("clapped").GetBoolean().Should().BeFalse();
    }

    [Fact]
    public async Task Reaction_totals_show_on_the_post_and_in_the_list()
    {
        var (id, slug, _) = await PublishedAsync("Zebra crossing notes");
        await Send(HttpMethod.Post, $"/api/v1/posts/{id}/reactions/clap", visitor: Visitor());
        await Send(HttpMethod.Post, $"/api/v1/posts/{id}/reactions/clap", visitor: Visitor());
        await Send(HttpMethod.Post, $"/api/v1/posts/{id}/reactions/insightful", visitor: Visitor());

        var post = await Json(await Send(HttpMethod.Get, $"/api/v1/posts/{slug}"));
        var listed = (await Json(await Send(HttpMethod.Get, "/api/v1/posts?q=Zebra"))).GetProperty("items")[0];

        post.GetProperty("clapCount").GetInt32().Should().Be(2);
        post.GetProperty("insightfulCount").GetInt32().Should().Be(1);
        listed.GetProperty("clapCount").GetInt32().Should().Be(2);
        listed.GetProperty("insightfulCount").GetInt32().Should().Be(1);
    }

    [Fact]
    public async Task A_visitor_sees_their_own_marks_and_a_stranger_sees_only_totals()
    {
        var (id, _, _) = await PublishedAsync("Whose mark");
        var visitor = Visitor();
        await Send(HttpMethod.Post, $"/api/v1/posts/{id}/reactions/insightful", visitor: visitor);

        var mine = await Json(await Send(HttpMethod.Get, $"/api/v1/posts/{id}/reactions", visitor: visitor));
        var stranger = await Json(await Send(HttpMethod.Get, $"/api/v1/posts/{id}/reactions"));

        mine.GetProperty("markedInsightful").GetBoolean().Should().BeTrue();
        stranger.GetProperty("markedInsightful").GetBoolean().Should().BeFalse();
        stranger.GetProperty("insightfulCount").GetInt32().Should().Be(1);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("not-an-id")]
    public async Task A_reaction_without_a_usable_visitor_id_is_refused(string? visitor)
    {
        var (id, _, _) = await PublishedAsync("Needs an id");

        var response = await Send(HttpMethod.Post, $"/api/v1/posts/{id}/reactions/clap", visitor: visitor);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task An_unknown_kind_of_reaction_is_refused()
    {
        var (id, _, _) = await PublishedAsync("Only two kinds");

        (await Send(HttpMethod.Post, $"/api/v1/posts/{id}/reactions/love", visitor: Visitor())).StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task A_post_that_is_taken_down_cannot_be_reacted_to()
    {
        var (id, _, token) = await PublishedAsync("Going away");
        await Send(HttpMethod.Post, $"/api/v1/posts/{id}/status", token, new { status = "Inactive" });

        (await Send(HttpMethod.Post, $"/api/v1/posts/{id}/reactions/clap", visitor: Visitor())).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await Send(HttpMethod.Get, $"/api/v1/posts/{id}/reactions")).StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task The_old_account_only_clap_route_is_gone()
    {
        var (id, _, token) = await PublishedAsync("No more piling on");

        var response = await Send(HttpMethod.Post, $"/api/v1/posts/{id}/claps?amount=10", token);

        ((int)response.StatusCode).Should().BeOneOf(404, 405);
    }

    [Fact]
    public async Task The_public_site_is_allowed_to_send_the_visitor_id_header()
    {
        var (id, _, _) = await PublishedAsync("Cross site");
        var preflight = new HttpRequestMessage(HttpMethod.Options, $"/api/v1/posts/{id}/reactions/clap");
        preflight.Headers.Add("Origin", ApiFactory.AllowedOrigin);
        preflight.Headers.Add("Access-Control-Request-Method", "POST");
        preflight.Headers.Add("Access-Control-Request-Headers", "x-visitor-id");
        preflight.Headers.Add("CF-Connecting-IP", NewIp());

        var response = await _client.SendAsync(preflight);

        response.Headers.GetValues("Access-Control-Allow-Origin").Should().ContainSingle(ApiFactory.AllowedOrigin);
        string.Join(",", response.Headers.GetValues("Access-Control-Allow-Headers")).ToLowerInvariant().Should().Contain("x-visitor-id");
    }

    [Fact]
    public async Task Reactions_from_one_address_are_rate_limited_like_other_writes()
    {
        var (id, _, _) = await PublishedAsync("Hammered");
        var ip = NewIp();
        var statuses = new List<HttpStatusCode>();

        for (var i = 0; i < 32; i++)
            statuses.Add((await Send(HttpMethod.Post, $"/api/v1/posts/{id}/reactions/clap", visitor: Visitor(), ip: ip)).StatusCode);

        statuses.Take(30).Should().OnlyContain(s => s == HttpStatusCode.OK);
        statuses.Last().Should().Be(HttpStatusCode.TooManyRequests);
    }

    // ---- Views --------------------------------------------------------------------------------

    private async Task<int> ViewsAsync(string id, string token)
    {
        var posts = await Json(await Send(HttpMethod.Get, "/api/v1/admin/posts?pageSize=100", token));
        return posts.GetProperty("items").EnumerateArray().Single(p => p.GetProperty("id").GetString() == id).GetProperty("views").GetInt32();
    }

    [Fact]
    public async Task Fetching_a_post_does_not_count_but_a_reported_view_counts_once_per_visitor()
    {
        var (id, slug, token) = await PublishedAsync("Honest numbers");
        var visitor = Visitor();

        for (var i = 0; i < 3; i++) await Send(HttpMethod.Get, $"/api/v1/posts/{slug}");
        (await ViewsAsync(id, token)).Should().Be(0);

        for (var i = 0; i < 3; i++)
            (await Send(HttpMethod.Post, $"/api/v1/posts/{id}/view", visitor: visitor)).StatusCode.Should().Be(HttpStatusCode.NoContent);
        await Send(HttpMethod.Post, $"/api/v1/posts/{id}/view", visitor: Visitor());

        (await ViewsAsync(id, token)).Should().Be(2);
    }

    [Theory]
    [InlineData("Mozilla/5.0 (compatible; Googlebot/2.1; +http://www.google.com/bot.html)")]
    [InlineData("facebookexternalhit/1.1")]
    [InlineData(null)]
    public async Task A_crawler_reporting_a_view_gets_the_same_answer_but_is_not_counted(string? userAgent)
    {
        var (id, _, token) = await PublishedAsync($"Bots {Guid.NewGuid():N}");

        var response = await Send(HttpMethod.Post, $"/api/v1/posts/{id}/view", visitor: Visitor(), userAgent: userAgent);

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await ViewsAsync(id, token)).Should().Be(0);
    }

    [Fact]
    public async Task The_writer_signed_in_on_the_site_is_not_counted_as_a_reader()
    {
        var (id, _, token) = await PublishedAsync("Not my own reader");

        await Send(HttpMethod.Post, $"/api/v1/posts/{id}/view", token, visitor: Visitor());

        (await ViewsAsync(id, token)).Should().Be(0);
    }

    [Fact]
    public async Task Reporting_a_view_of_a_post_that_does_not_exist_reveals_nothing()
    {
        (await Send(HttpMethod.Post, $"/api/v1/posts/{Guid.NewGuid()}/view", visitor: Visitor())).StatusCode.Should().Be(HttpStatusCode.NoContent);
    }
}
