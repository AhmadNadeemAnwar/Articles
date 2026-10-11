using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Articles.Application.Auth;
using Articles.Application.Auth.Dtos;
using Articles.Application.Comments;
using Articles.Application.Comments.Dtos;
using Articles.Application.Common;
using Articles.Application.Posts;
using Articles.Application.Portfolio;
using Articles.Application.Posts.Dtos;
using Articles.Domain.Interfaces;
using Articles.Tests.Admin;
using Articles.Tests.Portfolio;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Xunit;

namespace Articles.Tests.Api;

/// <summary>The API with an admin configured and a clock the tests control, so one-time codes are predictable.</summary>
public sealed class AdminApiFactory : ApiFactory
{
    public const string AdminEmail = "owner@example.com";
    public const string ReaderEmail = "reader@example.com";
    public const string Password = "correct horse battery staple";

    public string TotpSecret { get; } = TotpTestSecret.NewSecret();
    public ManualTimeProvider Clock { get; } = new(DateTimeOffset.UtcNow);
    public FakePortfolioStore Portfolio { get; } = new();

    private readonly SemaphoreSlim _seedGate = new(1, 1);
    private bool _seeded;

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);

        builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Admin:Emails:0"] = AdminEmail,
            ["Admin:TotpSecret"] = TotpSecret
        }));

        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<TimeProvider>();
            services.AddSingleton<TimeProvider>(Clock);

            // GitHub is replaced by an in-memory repository that applies the same version rules.
            services.RemoveAll<IPortfolioContentStore>();
            services.AddSingleton<IPortfolioContentStore>(Portfolio);
        });
    }

    /// <summary>Creates the admin and a reader once; safe to call from every test.</summary>
    public async Task EnsureUsersAsync()
    {
        await _seedGate.WaitAsync();
        try
        {
            if (_seeded) return;
            using var scope = Services.CreateScope();
            var auth = scope.ServiceProvider.GetRequiredService<IAuthService>();
            await auth.RegisterAsync(new RegisterRequest(AdminEmail, "the-owner", "The Owner", Password));
            await auth.RegisterAsync(new RegisterRequest(ReaderEmail, "a-reader", "A Reader", Password));
            _seeded = true;
        }
        finally
        {
            _seedGate.Release();
        }
    }

    /// <summary>A code the verifier has not seen: the clock jumps ahead two minutes first.</summary>
    public string FreshCode()
    {
        Clock.Advance(TimeSpan.FromMinutes(2));
        return TotpTestSecret.CodeFor(TotpSecret, Clock.GetUtcNow());
    }
}

public class AdminPipelineTests : IClassFixture<AdminApiFactory>
{
    private static int _ipCounter = 40;
    private readonly AdminApiFactory _factory;
    private readonly HttpClient _client;

    public AdminPipelineTests(AdminApiFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    private static string NewIp() => $"192.0.2.{Interlocked.Increment(ref _ipCounter)}";

    private Task<HttpResponseMessage> Send(HttpMethod method, string path, string? token = null, object? body = null, string? ip = null, string? origin = null)
    {
        var request = new HttpRequestMessage(method, path);
        request.Headers.Add("CF-Connecting-IP", ip ?? NewIp());
        if (token is not null) request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        if (origin is not null) request.Headers.Add("Origin", origin);
        if (body is not null) request.Content = JsonContent.Create(body);
        return _client.SendAsync(request);
    }

    private async Task<string> AdminTokenAsync()
    {
        await _factory.EnsureUsersAsync();
        var response = await Send(HttpMethod.Post, "/api/v1/admin/auth/login",
            body: new { email = AdminApiFactory.AdminEmail, code = _factory.FreshCode() });
        response.StatusCode.Should().Be(HttpStatusCode.OK, "valid admin credentials must sign in");
        return JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement.GetProperty("token").GetString()!;
    }

    private async Task<string> OrdinaryTokenAsync(string email)
    {
        await _factory.EnsureUsersAsync();
        var response = await Send(HttpMethod.Post, "/api/v1/auth/login", body: new { email, password = AdminApiFactory.Password });
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        return JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement.GetProperty("token").GetString()!;
    }

    // ---- Who gets in ---------------------------------------------------------------------

    [Fact]
    public async Task Admin_routes_refuse_anonymous_visitors()
    {
        foreach (var path in new[] { "/api/v1/admin/stats", "/api/v1/admin/posts", "/api/v1/admin/comments", "/api/v1/admin/tags", "/api/v1/admin/me" })
            (await Send(HttpMethod.Get, path)).StatusCode.Should().Be(HttpStatusCode.Unauthorized, path);
    }

    [Fact]
    public async Task The_admins_ordinary_sign_in_token_is_not_enough()
    {
        // Right account, right password: but no one-time code was verified, so no admin access.
        var token = await OrdinaryTokenAsync(AdminApiFactory.AdminEmail);

        (await Send(HttpMethod.Get, "/api/v1/admin/stats", token)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task A_token_with_the_code_claim_for_someone_not_on_the_admin_list_is_refused()
    {
        await _factory.EnsureUsersAsync();
        string forged;
        using (var scope = _factory.Services.CreateScope())
        {
            var reader = await scope.ServiceProvider.GetRequiredService<IUserRepository>().GetByEmailAsync(AdminApiFactory.ReaderEmail);
            forged = scope.ServiceProvider.GetRequiredService<ITokenService>().CreateAdminSession(reader!).Token;
        }

        (await Send(HttpMethod.Get, "/api/v1/admin/stats", forged)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task A_garbage_token_is_unauthorised()
    {
        (await Send(HttpMethod.Get, "/api/v1/admin/stats", "not.a.jwt")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Removing_an_admin_from_the_list_revokes_their_live_session_at_once()
    {
        var token = await AdminTokenAsync();
        (await Send(HttpMethod.Get, "/api/v1/admin/me", token)).StatusCode.Should().Be(HttpStatusCode.OK);

        var config = (IConfigurationRoot)_factory.Services.GetRequiredService<IConfiguration>();
        config["Admin:Emails:0"] = "someone-else@example.com";
        config.Reload();
        try
        {
            (await Send(HttpMethod.Get, "/api/v1/admin/me", token)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        }
        finally
        {
            config["Admin:Emails:0"] = AdminApiFactory.AdminEmail;
            config.Reload();
        }

        (await Send(HttpMethod.Get, "/api/v1/admin/me", token)).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    // ---- Admin sign-in -------------------------------------------------------------------

    [Fact]
    public async Task Admin_sign_in_returns_a_working_session_for_the_listed_account()
    {
        var token = await AdminTokenAsync();

        var me = await Send(HttpMethod.Get, "/api/v1/admin/me", token);

        me.StatusCode.Should().Be(HttpStatusCode.OK);
        (await me.Content.ReadAsStringAsync()).Should().Contain(AdminApiFactory.AdminEmail);
    }

    [Fact]
    public async Task Admin_sessions_are_much_shorter_than_ordinary_ones()
    {
        await _factory.EnsureUsersAsync();
        var response = await Send(HttpMethod.Post, "/api/v1/admin/auth/login",
            body: new { email = AdminApiFactory.AdminEmail, code = _factory.FreshCode() });

        var expires = JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement.GetProperty("expiresAt").GetDateTimeOffset();

        (expires - DateTimeOffset.UtcNow).Should().BeCloseTo(TimeSpan.FromHours(2), TimeSpan.FromMinutes(5));
    }

    [Fact]
    public async Task A_wrong_code_is_refused_with_a_message_that_does_not_name_the_failed_factor()
    {
        await _factory.EnsureUsersAsync();

        var response = await Send(HttpMethod.Post, "/api/v1/admin/auth/login",
            body: new { email = AdminApiFactory.AdminEmail, code = "000000" });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await response.Content.ReadAsStringAsync()).Should().Contain("Invalid email or code");
    }

    [Fact]
    public async Task An_unknown_email_with_a_valid_code_gets_the_identical_response()
    {
        await _factory.EnsureUsersAsync();
        var wrongEmail = await Send(HttpMethod.Post, "/api/v1/admin/auth/login",
            body: new { email = "nobody@example.com", code = _factory.FreshCode() });
        var wrongCode = await Send(HttpMethod.Post, "/api/v1/admin/auth/login",
            body: new { email = AdminApiFactory.AdminEmail, code = "000000" });

        wrongEmail.StatusCode.Should().Be(wrongCode.StatusCode);
        (await wrongEmail.Content.ReadAsStringAsync()).Should().Be(await wrongCode.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task A_code_cannot_be_used_to_sign_in_twice()
    {
        await _factory.EnsureUsersAsync();
        var code = _factory.FreshCode();
        var body = new { email = AdminApiFactory.AdminEmail, code };

        (await Send(HttpMethod.Post, "/api/v1/admin/auth/login", body: body)).StatusCode.Should().Be(HttpStatusCode.OK);
        (await Send(HttpMethod.Post, "/api/v1/admin/auth/login", body: body)).StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task A_malformed_code_is_rejected_before_any_check_runs()
    {
        var response = await Send(HttpMethod.Post, "/api/v1/admin/auth/login",
            body: new { email = AdminApiFactory.AdminEmail, code = "12ab56" });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await response.Content.ReadAsStringAsync()).Should().Contain("6 digits");
    }

    [Fact]
    public async Task Admin_sign_in_is_limited_to_five_attempts_a_minute_per_client()
    {
        var ip = NewIp();
        var attempt = () => Send(HttpMethod.Post, "/api/v1/admin/auth/login", ip: ip,
            body: new { email = $"x{Guid.NewGuid():N}@example.com", code = "000000" });

        for (var i = 0; i < 5; i++) (await attempt()).StatusCode.Should().Be(HttpStatusCode.BadRequest);

        (await attempt()).StatusCode.Should().Be(HttpStatusCode.TooManyRequests);
    }

    // ---- Doing things as the admin ---------------------------------------------------------

    [Fact]
    public async Task The_dashboard_returns_site_wide_figures()
    {
        var token = await AdminTokenAsync();

        var response = await Send(HttpMethod.Get, "/api/v1/admin/stats", token);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;
        json.GetProperty("users").GetInt32().Should().BeGreaterThanOrEqualTo(2);
        json.GetProperty("publishedLast30Days").GetArrayLength().Should().Be(30);
    }

    [Fact]
    public async Task An_admin_can_find_and_remove_a_comment_and_a_post_over_http()
    {
        var token = await AdminTokenAsync();
        Guid postId, commentId;

        using (var scope = _factory.Services.CreateScope())
        {
            var users = scope.ServiceProvider.GetRequiredService<IUserRepository>();
            var author = (await users.GetByEmailAsync(AdminApiFactory.AdminEmail))!;
            var reader = (await users.GetByEmailAsync(AdminApiFactory.ReaderEmail))!;
            var posts = scope.ServiceProvider.GetRequiredService<IPostService>();

            var draft = await posts.CreateDraftAsync(new CreatePostRequest("Moderated post", null, TestDatabase.Document("Some body."), null, ["moderation"]), author.Id);
            await posts.PublishAsync(draft.Id, author.Id);
            postId = draft.Id;
            commentId = (await scope.ServiceProvider.GetRequiredService<ICommentService>()
                .AddAsync(postId, new CreateCommentRequest("Questionable comment", null), reader.Id)).Id;
        }

        var listed = await Send(HttpMethod.Get, "/api/v1/admin/posts?q=Moderated", token);
        (await listed.Content.ReadAsStringAsync()).Should().Contain("Moderated post");

        var comments = await Send(HttpMethod.Get, "/api/v1/admin/comments", token);
        (await comments.Content.ReadAsStringAsync()).Should().Contain("Questionable comment");

        (await Send(HttpMethod.Delete, $"/api/v1/admin/comments/{commentId}", token)).StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await Send(HttpMethod.Post, $"/api/v1/admin/posts/{postId}/unpublish", token)).StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await Send(HttpMethod.Delete, $"/api/v1/admin/posts/{postId}", token)).StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await Send(HttpMethod.Delete, $"/api/v1/admin/posts/{postId}", token)).StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Tags_can_be_renamed_merged_and_deleted_over_http()
    {
        var token = await AdminTokenAsync();
        using (var scope = _factory.Services.CreateScope())
        {
            var author = (await scope.ServiceProvider.GetRequiredService<IUserRepository>().GetByEmailAsync(AdminApiFactory.AdminEmail))!;
            var posts = scope.ServiceProvider.GetRequiredService<IPostService>();
            var draft = await posts.CreateDraftAsync(new CreatePostRequest("Tagged", null, TestDatabase.Document("Body."), null, ["http-old", "http-new", "http-spare"]), author.Id);
            await posts.PublishAsync(draft.Id, author.Id);
        }

        var tags = JsonDocument.Parse(await (await Send(HttpMethod.Get, "/api/v1/admin/tags", token)).Content.ReadAsStringAsync()).RootElement;
        Guid IdOf(string slug) => tags.EnumerateArray().Single(t => t.GetProperty("slug").GetString() == slug).GetProperty("id").GetGuid();

        var rename = await Send(HttpMethod.Put, $"/api/v1/admin/tags/{IdOf("http-new")}", token, new { name = "Shiny New" });
        rename.StatusCode.Should().Be(HttpStatusCode.OK);
        (await rename.Content.ReadAsStringAsync()).Should().Contain("Shiny New").And.Contain("http-new");

        (await Send(HttpMethod.Post, $"/api/v1/admin/tags/{IdOf("http-old")}/merge", token, new { targetTagId = IdOf("http-new") }))
            .StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await Send(HttpMethod.Delete, $"/api/v1/admin/tags/{IdOf("http-spare")}", token)).StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await Send(HttpMethod.Post, $"/api/v1/admin/tags/{Guid.NewGuid()}/merge", token, new { targetTagId = IdOf("http-new") }))
            .StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    // ---- Where it can be reached from -----------------------------------------------------

    [Fact]
    public async Task The_admin_site_origin_is_allowed_and_a_stranger_is_not()
    {
        var allowed = await Send(HttpMethod.Options, "/api/v1/admin/stats", origin: "https://admin.ahmadnadeem.dev");
        var stranger = await Send(HttpMethod.Options, "/api/v1/admin/stats", origin: "https://evil.example");

        // A preflight needs the request-method header to be treated as one.
        var preflight = new HttpRequestMessage(HttpMethod.Options, "/api/v1/admin/stats");
        preflight.Headers.Add("Origin", "https://admin.ahmadnadeem.dev");
        preflight.Headers.Add("Access-Control-Request-Method", "GET");
        preflight.Headers.Add("Access-Control-Request-Headers", "authorization");
        preflight.Headers.Add("CF-Connecting-IP", NewIp());
        var real = await _client.SendAsync(preflight);

        real.Headers.GetValues("Access-Control-Allow-Origin").Should().ContainSingle("https://admin.ahmadnadeem.dev");
        stranger.Headers.Contains("Access-Control-Allow-Origin").Should().BeFalse();
        allowed.Should().NotBeNull();
    }

    [Fact]
    public async Task Admin_responses_are_never_cached()
    {
        var token = await AdminTokenAsync();

        var response = await Send(HttpMethod.Get, "/api/v1/admin/stats", token);

        response.Headers.CacheControl!.NoStore.Should().BeTrue();
    }
}

public class AdminPortfolioHttpTests : IClassFixture<AdminApiFactory>
{
    private static int _ipCounter = 140;
    private readonly AdminApiFactory _factory;
    private readonly HttpClient _client;

    public AdminPortfolioHttpTests(AdminApiFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
        _factory.Portfolio.IsConfigured = true;
    }

    private static string NewIp() => $"192.0.2.{Interlocked.Increment(ref _ipCounter)}";

    private Task<HttpResponseMessage> Send(HttpMethod method, string path, string? token, object? body = null)
    {
        var request = new HttpRequestMessage(method, path);
        request.Headers.Add("CF-Connecting-IP", NewIp());
        if (token is not null) request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        if (body is not null) request.Content = JsonContent.Create(body);
        return _client.SendAsync(request);
    }

    private async Task<string> AdminTokenAsync()
    {
        await _factory.EnsureUsersAsync();
        var response = await Send(HttpMethod.Post, "/api/v1/admin/auth/login", null,
            new { email = AdminApiFactory.AdminEmail, code = _factory.FreshCode() });
        return JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement.GetProperty("token").GetString()!;
    }

    private static object Entry(string? sha, string title = "From the portal") => new
    {
        sha,
        frontmatter = new { title, description = "Described", date = "2026-10-01", tags = new[] { "meta" }, draft = false },
        body = "Written in the admin portal."
    };

    [Fact]
    public async Task Portfolio_routes_need_an_admin_session()
    {
        foreach (var path in new[] { "/api/v1/admin/portfolio/status", "/api/v1/admin/portfolio/blog", "/api/v1/admin/portfolio/blog/x" })
            (await Send(HttpMethod.Get, path, null)).StatusCode.Should().Be(HttpStatusCode.Unauthorized, path);

        var ordinary = await Send(HttpMethod.Post, "/api/v1/auth/login", null, new { email = AdminApiFactory.AdminEmail, password = AdminApiFactory.Password });
        var ordinaryToken = JsonDocument.Parse(await ordinary.Content.ReadAsStringAsync()).RootElement.GetProperty("token").GetString();
        (await Send(HttpMethod.Put, "/api/v1/admin/portfolio/blog/sneaky", ordinaryToken, Entry(null))).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        _factory.Portfolio.Files.Should().NotContainKey("src/content/blog/sneaky.md");
    }

    [Fact]
    public async Task Status_reports_whether_github_is_connected()
    {
        var token = await AdminTokenAsync();

        var response = await Send(HttpMethod.Get, "/api/v1/admin/portfolio/status", token);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;
        json.GetProperty("configured").GetBoolean().Should().BeTrue();
        json.GetProperty("repo").GetString().Should().Be("owner/portfolio");
    }

    [Fact]
    public async Task An_entry_can_be_created_read_edited_and_deleted_over_http()
    {
        var token = await AdminTokenAsync();
        var slug = $"http-{Guid.NewGuid():N}"[..20];

        var created = await Send(HttpMethod.Put, $"/api/v1/admin/portfolio/blog/{slug}", token, Entry(null));
        created.StatusCode.Should().Be(HttpStatusCode.OK, await created.Content.ReadAsStringAsync());
        var sha = JsonDocument.Parse(await created.Content.ReadAsStringAsync()).RootElement.GetProperty("sha").GetString();
        _factory.Portfolio.Files[$"src/content/blog/{slug}.md"].Should().Contain("title: From the portal").And.Contain("tags: [meta]");

        var read = await Send(HttpMethod.Get, $"/api/v1/admin/portfolio/blog/{slug}", token);
        var readJson = JsonDocument.Parse(await read.Content.ReadAsStringAsync()).RootElement;
        readJson.GetProperty("frontmatter").GetProperty("title").GetString().Should().Be("From the portal");
        readJson.GetProperty("body").GetString().Should().Contain("Written in the admin portal.");

        var list = await Send(HttpMethod.Get, "/api/v1/admin/portfolio/blog", token);
        (await list.Content.ReadAsStringAsync()).Should().Contain(slug);

        var edited = await Send(HttpMethod.Put, $"/api/v1/admin/portfolio/blog/{slug}", token, Entry(sha, "Edited title"));
        edited.StatusCode.Should().Be(HttpStatusCode.OK);
        var newSha = JsonDocument.Parse(await edited.Content.ReadAsStringAsync()).RootElement.GetProperty("sha").GetString();

        (await Send(HttpMethod.Delete, $"/api/v1/admin/portfolio/blog/{slug}?sha={Uri.EscapeDataString(newSha!)}", token)).StatusCode.Should().Be(HttpStatusCode.NoContent);
        _factory.Portfolio.Files.Should().NotContainKey($"src/content/blog/{slug}.md");
    }

    [Fact]
    public async Task Saving_a_stale_version_returns_conflict_and_leaves_the_file_alone()
    {
        var token = await AdminTokenAsync();
        var slug = $"stale-{Guid.NewGuid():N}"[..18];
        var created = await Send(HttpMethod.Put, $"/api/v1/admin/portfolio/blog/{slug}", token, Entry(null));
        var sha = JsonDocument.Parse(await created.Content.ReadAsStringAsync()).RootElement.GetProperty("sha").GetString();
        _factory.Portfolio.Seed($"src/content/blog/{slug}.md", "---\ntitle: Changed elsewhere\ndescription: d\ndate: 2026-10-02\n---\n");

        var stale = await Send(HttpMethod.Put, $"/api/v1/admin/portfolio/blog/{slug}", token, Entry(sha, "My stale edit"));

        stale.StatusCode.Should().Be(HttpStatusCode.Conflict);
        _factory.Portfolio.Files[$"src/content/blog/{slug}.md"].Should().Contain("Changed elsewhere");
    }

    [Fact]
    public async Task Invalid_entries_are_refused_with_the_reasons()
    {
        var token = await AdminTokenAsync();

        var response = await Send(HttpMethod.Put, "/api/v1/admin/portfolio/projects/bad-one", token, new
        {
            sha = (string?)null,
            frontmatter = new { title = "No summary", date = "not-a-date", repo = "javascript:alert(1)" },
            body = "x"
        });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var text = await response.Content.ReadAsStringAsync();
        text.Should().Contain("summary is required").And.Contain("YYYY-MM-DD").And.Contain("http");
        _factory.Portfolio.Files.Should().NotContainKey("src/content/projects/bad-one.md");
    }

    [Fact]
    public async Task Bad_names_and_unknown_collections_are_refused()
    {
        var token = await AdminTokenAsync();

        (await Send(HttpMethod.Put, "/api/v1/admin/portfolio/blog/Bad%20Name", token, Entry(null))).StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await Send(HttpMethod.Put, "/api/v1/admin/portfolio/blog/..%2f..%2fetc", token, Entry(null))).StatusCode.Should().BeOneOf(HttpStatusCode.BadRequest, HttpStatusCode.NotFound);
        (await Send(HttpMethod.Put, "/api/v1/admin/portfolio/secrets/x", token, Entry(null))).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await Send(HttpMethod.Get, "/api/v1/admin/portfolio/blog/does-not-exist", token)).StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task While_github_is_not_configured_the_portal_gets_a_clear_503()
    {
        var token = await AdminTokenAsync();
        _factory.Portfolio.IsConfigured = false;
        try
        {
            var status = await Send(HttpMethod.Get, "/api/v1/admin/portfolio/status", token);
            JsonDocument.Parse(await status.Content.ReadAsStringAsync()).RootElement.GetProperty("configured").GetBoolean().Should().BeFalse();

            var list = await Send(HttpMethod.Get, "/api/v1/admin/portfolio/blog", token);
            list.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
            (await list.Content.ReadAsStringAsync()).Should().Contain("not set up");
        }
        finally
        {
            _factory.Portfolio.IsConfigured = true;
        }
    }
}
