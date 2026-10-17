using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Articles.Application.Engagement;
using Articles.Application.Posts;
using Articles.Application.Posts.Dtos;
using Articles.Domain.Exceptions;
using Articles.Infrastructure.Persistence.Repositories;
using Articles.Tests.Api;
using Xunit;

namespace Articles.Tests.Engagement;

/// <summary>The coarse facts kept about a view: a country code, a kind of device and a site name, and never anything finer.</summary>
public class ViewFactsTests
{
    [Theory]
    [InlineData("MY", "MY")]
    [InlineData(" pk ", "PK")]
    [InlineData("XX", "")]
    [InlineData("", "")]
    [InlineData(null, "")]
    [InlineData("MYS", "")]
    [InlineData("1.2.3.4", "")]
    [InlineData("<b>", "")]
    public void Only_a_two_letter_country_code_is_kept(string? value, string expected) =>
        ViewFacts.Country(value).Should().Be(expected);

    [Theory]
    [InlineData("Mozilla/5.0 (iPhone; CPU iPhone OS 17_0 like Mac OS X) AppleWebKit/605.1.15 Mobile/15E148", "phone")]
    [InlineData("Mozilla/5.0 (Linux; Android 14; Pixel 8) AppleWebKit/537.36 Chrome/120 Mobile Safari/537.36", "phone")]
    [InlineData("Mozilla/5.0 (iPad; CPU OS 17_0 like Mac OS X) AppleWebKit/605.1.15", "tablet")]
    [InlineData("Mozilla/5.0 (Linux; Android 13; SM-X700) AppleWebKit/537.36 Chrome/120 Safari/537.36", "tablet")]
    [InlineData("Mozilla/5.0 (Windows NT 10.0; Win64; x64) Chrome/141.0.0.0 Safari/537.36", "computer")]
    [InlineData("Mozilla/5.0 (Macintosh; Intel Mac OS X 14_0) Safari/605.1.15", "computer")]
    [InlineData(null, "computer")]
    public void The_kind_of_device_is_worked_out_and_the_browser_string_is_not_kept(string? userAgent, string expected) =>
        ViewFacts.Device(userAgent).Should().Be(expected);

    [Theory]
    [InlineData("www.google.com", "google.com")]
    [InlineData("LinkedIn.com", "linkedin.com")]
    [InlineData("t.co", "t.co")]
    [InlineData("", "")]
    [InlineData(null, "")]
    [InlineData("articles.ahmadnadeem.dev", "")]
    [InlineData("ahmadnadeem.dev", "")]
    [InlineData("localhost", "")]
    [InlineData("https://google.com/search?q=private", "")]
    [InlineData("google.com/path", "")]
    [InlineData("evil..com", "")]
    [InlineData("<script>.com", "")]
    public void Only_the_bare_name_of_another_site_is_kept(string? host, string expected) =>
        ViewFacts.Source(host, ReactionService.OwnSites).Should().Be(expected);
}

public class ViewStatsTests : IDisposable
{
    private const string Computer = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) Chrome/141.0.0.0 Safari/537.36";
    private const string Phone = "Mozilla/5.0 (iPhone; CPU iPhone OS 17_0 like Mac OS X) AppleWebKit/605.1.15 Mobile/15E148";

    private readonly TestDatabase _fixture = new();
    private readonly ManualTimeProvider _clock = new(new DateTimeOffset(2026, 10, 17, 9, 0, 0, TimeSpan.Zero));
    private readonly ReactionService _reactions;
    private readonly ViewStatsService _stats;
    private readonly PostService _posts;

    public ViewStatsTests()
    {
        _posts = new PostService(_fixture.Posts, _fixture.Tags, _fixture.Engagement, _fixture.Db);
        var repository = new ReactionRepository(_fixture.Db);
        _reactions = new ReactionService(repository, _fixture.Posts, _clock);
        _stats = new ViewStatsService(repository, _fixture.Posts, _clock);
    }

    public void Dispose() => _fixture.Dispose();

    private async Task<Guid> PublishedAsync(string handle, string title)
    {
        var author = await _fixture.AddUserAsync(handle);
        var draft = await _posts.CreateDraftAsync(new CreatePostRequest(title, null, TestDatabase.Document("Body."), null, []), author.Id);
        await _posts.PublishAsync(draft.Id, author.Id);
        return draft.Id;
    }

    // Readers are told apart by a random id their browser makes up; a name here stands for one such id.
    private readonly Dictionary<string, string> _ids = [];
    private string Id(string name) => _ids.TryGetValue(name, out var id) ? id : _ids[name] = Guid.NewGuid().ToString();

    private Task View(Guid post, string visitor, string agent = Computer, string? country = null, string? from = null) =>
        _reactions.RecordViewAsync(post, Id(visitor), agent, null, default, country, from);

    [Fact]
    public async Task Each_day_counts_its_readers_and_days_with_none_show_as_zero()
    {
        var post = await PublishedAsync("ada", "First");
        await View(post, "a");
        await View(post, "b");
        _clock.Advance(TimeSpan.FromDays(2));
        await View(post, "c");

        var stats = await _stats.GetAsync(post, 7);

        stats.Views.Should().Be(3);
        stats.Daily.Should().HaveCount(7);
        stats.Daily[^1].Should().Be(new DayViews("2026-10-19", 1, 0));
        stats.Daily[^2].Views.Should().Be(0);
        stats.Daily[^3].Should().Be(new DayViews("2026-10-17", 2, 0));
    }

    [Fact]
    public async Task A_reader_coming_back_on_another_day_counts_as_returning()
    {
        var post = await PublishedAsync("ada", "First");
        await View(post, "a");
        _clock.Advance(TimeSpan.FromDays(1));
        await View(post, "a");
        await View(post, "b");

        var stats = await _stats.GetAsync(post, 7);

        stats.Views.Should().Be(3);
        stats.Returning.Should().Be(1);
        stats.Daily[^1].Should().Be(new DayViews("2026-10-18", 2, 1));
    }

    [Fact]
    public async Task Readers_are_split_by_country_device_and_where_they_came_from()
    {
        var post = await PublishedAsync("ada", "First");
        await View(post, "a", Computer, "MY", "www.google.com");
        await View(post, "b", Phone, "MY", "linkedin.com");
        await View(post, "c", Phone, "PK", null);
        await View(post, "d", Phone, null, "articles.ahmadnadeem.dev");

        var stats = await _stats.GetAsync(post, 30);

        stats.Countries.Should().Equal(new ViewShare("MY", 2), new ViewShare("", 1), new ViewShare("PK", 1));
        stats.Devices.Should().Equal(new ViewShare("phone", 3), new ViewShare("computer", 1));
        stats.Sources.Should().Equal(new ViewShare("", 2), new ViewShare("google.com", 1), new ViewShare("linkedin.com", 1));
    }

    [Fact]
    public async Task Nothing_that_leads_back_to_a_reader_is_stored_with_the_counts()
    {
        var post = await PublishedAsync("ada", "First");
        await View(post, "reader", Computer, "MY", "google.com");

        var row = _fixture.Db.PostViewStats.Single();
        new[] { row.Country, row.Device, row.Source }.Should().NotContain(v => v.Contains(Id("reader")) || v.Contains("Chrome"));
        row.Count.Should().Be(1);
    }

    [Fact]
    public async Task A_view_that_is_not_counted_adds_nothing_to_the_daily_figures()
    {
        var post = await PublishedAsync("ada", "First");
        await View(post, "a");
        await View(post, "a");
        await _reactions.RecordViewAsync(post, Id("bot"), "Googlebot/2.1", null);

        (await _stats.GetAsync(post, 7)).Views.Should().Be(1);
    }

    [Fact]
    public async Task All_posts_together_show_the_most_read_first_and_one_post_alone_shows_no_list()
    {
        var first = await PublishedAsync("ada", "First");
        var second = await PublishedAsync("bob", "Second");
        await View(first, "a");
        await View(second, "a");
        await View(second, "b");

        var all = await _stats.GetAsync(null, 30);
        all.Views.Should().Be(3);
        all.TopPosts.Select(p => (p.Title, p.Views)).Should().Equal(("Second", 2), ("First", 1));

        (await _stats.GetAsync(first, 30)).TopPosts.Should().BeEmpty();
    }

    [Fact]
    public async Task The_all_time_total_includes_readers_from_before_daily_figures_were_kept()
    {
        var post = await PublishedAsync("ada", "First");
        var other = await PublishedAsync("bob", "Second");
        await View(post, "a");
        await View(other, "b");
        // Readers counted before this release are only in each post's running total, not in the daily figures.
        for (var i = 0; i < 5; i++) (await _fixture.Posts.GetByIdAsync(post))!.RegisterView();
        await _fixture.Db.SaveChangesAsync();

        (await _stats.GetAsync(post, 7)).Should().Match<ViewStatsDto>(s => s.Views == 1 && s.AllTime == 6);
        (await _stats.GetAsync(null, 7)).Should().Match<ViewStatsDto>(s => s.Views == 2 && s.AllTime == 7);
    }

    [Fact]
    public async Task Days_outside_the_period_are_left_out()
    {
        var post = await PublishedAsync("ada", "First");
        await View(post, "a");
        _clock.Advance(TimeSpan.FromDays(10));
        await View(post, "b");

        (await _stats.GetAsync(post, 7)).Views.Should().Be(1);
        (await _stats.GetAsync(post, 30)).Views.Should().Be(2);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(14)]
    [InlineData(365)]
    public async Task Only_the_offered_periods_are_accepted(int days)
    {
        (await Record.ExceptionAsync(() => _stats.GetAsync(null, days))).Should().BeOfType<DomainException>();
    }

    [Fact]
    public async Task An_unknown_post_is_not_found()
    {
        (await Record.ExceptionAsync(() => _stats.GetAsync(Guid.NewGuid(), 30))).Should().BeOfType<NotFoundException>();
    }
}

/// <summary>Daily views through the real HTTP pipeline: the page's country and source reach the counts, and only the owner can read them.</summary>
public class ViewStatsPipelineTests : IClassFixture<AdminApiFactory>
{
    private static int _ipCounter = 30;
    private readonly AdminApiFactory _factory;
    private readonly HttpClient _client;

    public ViewStatsPipelineTests(AdminApiFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    private Task<HttpResponseMessage> Send(HttpMethod method, string path, string? token = null, object? body = null, Dictionary<string, string>? headers = null)
    {
        var request = new HttpRequestMessage(method, path);
        request.Headers.Add("CF-Connecting-IP", $"198.51.100.{Interlocked.Increment(ref _ipCounter) % 250}");
        if (token is not null) request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        foreach (var (name, value) in headers ?? []) request.Headers.TryAddWithoutValidation(name, value);
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

    [Fact]
    public async Task A_readers_view_with_its_country_and_source_shows_in_the_owners_figures()
    {
        var token = await AdminTokenAsync();
        var post = await Json(await Send(HttpMethod.Post, "/api/v1/posts", token,
            new { title = $"Views {Guid.NewGuid():N}"[..18], subtitle = (string?)null, contentJson = TestDatabase.Document("Body."), coverImageUrl = (string?)null, tags = Array.Empty<string>(), categoryId = (string?)null }));
        var id = post.GetProperty("id").GetString()!;
        await Send(HttpMethod.Post, $"/api/v1/posts/{id}/status", token, new { status = "Published" });

        (await Send(HttpMethod.Post, $"/api/v1/posts/{id}/view", headers: new()
        {
            ["X-Visitor-Id"] = Guid.NewGuid().ToString(),
            ["User-Agent"] = "Mozilla/5.0 (iPhone; CPU iPhone OS 17_0 like Mac OS X) Mobile/15E148",
            ["X-Viewer-Country"] = "MY",
            ["X-Referrer-Host"] = "www.linkedin.com",
        })).StatusCode.Should().Be(HttpStatusCode.NoContent);

        var stats = await Json(await Send(HttpMethod.Get, $"/api/v1/admin/views?postId={id}&days=7", token));
        stats.GetProperty("views").GetInt32().Should().Be(1);
        stats.GetProperty("countries")[0].GetProperty("key").GetString().Should().Be("MY");
        stats.GetProperty("devices")[0].GetProperty("key").GetString().Should().Be("phone");
        stats.GetProperty("sources")[0].GetProperty("key").GetString().Should().Be("linkedin.com");
        stats.GetProperty("daily").GetArrayLength().Should().Be(7);
    }

    [Fact]
    public async Task Only_the_owner_can_see_the_figures()
    {
        (await Send(HttpMethod.Get, "/api/v1/admin/views")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task A_period_that_is_not_offered_is_a_bad_request()
    {
        (await Send(HttpMethod.Get, "/api/v1/admin/views?days=365", await AdminTokenAsync())).StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }
}
