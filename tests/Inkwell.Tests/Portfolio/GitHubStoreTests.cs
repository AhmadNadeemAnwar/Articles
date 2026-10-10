using System.Net;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using Inkwell.Domain.Exceptions;
using Inkwell.Infrastructure.Portfolio;
using Inkwell.Tests.Admin;
using Inkwell.Tests.Security;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Inkwell.Tests.Portfolio;

public class GitHubStoreTests
{
    private const string Token = "github_pat_SECRET_TOKEN_VALUE";

    private static GitHubPortfolioContentStore Store(HttpMessageHandler handler, PortfolioOptions? options = null) =>
        new(new HttpClient(handler),
            new TestOptionsMonitor<PortfolioOptions>(options ?? new PortfolioOptions { Repo = "owner/portfolio", Branch = "main", Token = Token }),
            NullLogger<GitHubPortfolioContentStore>.Instance);

    private static HttpResponseMessage Json(HttpStatusCode status, object body) =>
        new(status) { Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json") };

    private static string B64(string text) => Convert.ToBase64String(Encoding.UTF8.GetBytes(text));

    // ---- Configuration -------------------------------------------------------------------

    [Theory]
    [InlineData("", "owner/repo", false)]
    [InlineData("token", "", false)]
    [InlineData("token", "just-a-name", false)]
    [InlineData("token", "owner/repo/extra", false)]
    [InlineData("token", "owner/re po", false)]
    [InlineData("token", "../other", false)]
    [InlineData("token", "owner/..", false)]
    [InlineData("token", "owner/.", false)]
    [InlineData("token", "own.er/repo", false)]
    [InlineData("token", "-owner/repo", false)]
    [InlineData("token", "owner/repo", true)]
    [InlineData("token", "Some-Owner/my.repo_1", true)]
    public void It_is_configured_only_with_a_token_and_a_well_formed_repository(string token, string repo, bool expected)
    {
        var store = Store(new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)), new PortfolioOptions { Token = token, Repo = repo });

        store.IsConfigured.Should().Be(expected);
        (store.Repo is not null).Should().Be(expected, "an unconfigured store must not advertise a repository");
    }

    [Fact]
    public async Task Nothing_is_sent_to_github_while_unconfigured()
    {
        var handler = new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.OK));
        var store = Store(handler, new PortfolioOptions());

        (await Record.ExceptionAsync(() => store.GetAsync("src/content/blog/a.md"))).Should().BeOfType<ServiceUnavailableException>();
        handler.Calls.Should().BeEmpty();
    }

    [Theory]
    [InlineData("https://api.github.com", true)]
    [InlineData("https://github.example.com/api/v3/", true)]
    [InlineData("http://localhost:4599", true)]
    [InlineData("http://127.0.0.1:4599/", true)]
    [InlineData("http://github.example.com", false)]
    [InlineData("http://api.github.com", false)]
    [InlineData("ftp://api.github.com", false)]
    [InlineData("not a url", false)]
    public void The_token_is_only_ever_sent_over_https_or_to_this_machine(string baseUrl, bool accepted)
    {
        var store = Store(new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)),
            new PortfolioOptions { Repo = "owner/repo", Token = Token, ApiBaseUrl = baseUrl });

        store.IsConfigured.Should().Be(accepted);
    }

    [Fact]
    public async Task A_custom_api_address_is_used_for_requests()
    {
        var handler = new StubHandler(_ => Json(HttpStatusCode.NotFound, new { message = "Not Found" }));
        var store = Store(handler, new PortfolioOptions { Repo = "owner/repo", Token = Token, ApiBaseUrl = "http://localhost:4599/" });

        await store.GetAsync("src/content/blog/a.md");

        handler.Calls.Single().Request.RequestUri!.ToString().Should().StartWith("http://localhost:4599/repos/owner/repo/contents/");
    }

    // ---- Reading -------------------------------------------------------------------------

    [Fact]
    public async Task Reading_a_file_decodes_it_and_sends_the_right_headers()
    {
        var handler = new StubHandler(_ => Json(HttpStatusCode.OK, new
        {
            type = "file", name = "hello.md", path = "src/content/blog/hello.md", sha = "abc123",
            content = B64("---\ntitle: Héllo — ✓\n---\n").Insert(8, "\n")   // GitHub wraps base64 with newlines
        }));

        var file = await Store(handler).GetAsync("src/content/blog/hello.md");

        file!.Content.Should().Be("---\ntitle: Héllo — ✓\n---\n");
        file.Sha.Should().Be("abc123");

        var request = handler.Calls.Single().Request;
        request.RequestUri!.ToString().Should().Be("https://api.github.com/repos/owner/portfolio/contents/src/content/blog/hello.md?ref=main");
        request.Headers.Authorization!.Scheme.Should().Be("Bearer");
        request.Headers.Authorization.Parameter.Should().Be(Token);
        request.Headers.UserAgent.ToString().Should().Contain("articles-admin");
        request.Headers.Accept.ToString().Should().Contain("application/vnd.github+json");
        request.Headers.GetValues("X-GitHub-Api-Version").Should().ContainSingle();
    }

    [Fact]
    public async Task A_missing_file_is_null_not_an_error() =>
        (await Store(new StubHandler(_ => Json(HttpStatusCode.NotFound, new { message = "Not Found" }))).GetAsync("src/content/blog/none.md")).Should().BeNull();

    [Fact]
    public async Task A_directory_where_a_file_was_expected_is_treated_as_missing() =>
        (await Store(new StubHandler(_ => Json(HttpStatusCode.OK, new[] { new { type = "file", name = "x.md", path = "p", sha = "s" } })))
            .GetAsync("src/content/blog")).Should().BeNull();

    [Fact]
    public async Task Listing_returns_files_only()
    {
        var handler = new StubHandler(_ => Json(HttpStatusCode.OK, new object[]
        {
            new { type = "file", name = "a.md", path = "src/content/blog/a.md", sha = "s1" },
            new { type = "dir", name = "drafts", path = "src/content/blog/drafts", sha = "s2" },
            new { type = "file", name = "b.mdx", path = "src/content/blog/b.mdx", sha = "s3" },
        }));

        var files = await Store(handler).ListAsync("src/content/blog");

        files.Select(f => f.Name).Should().Equal("a.md", "b.mdx");
        files.Should().OnlyContain(f => f.Content == null);
    }

    [Fact]
    public async Task Listing_a_directory_that_does_not_exist_yet_is_empty() =>
        (await Store(new StubHandler(_ => Json(HttpStatusCode.NotFound, new { message = "Not Found" }))).ListAsync("src/content/blog")).Should().BeEmpty();

    // ---- Writing -------------------------------------------------------------------------

    [Fact]
    public async Task Creating_a_file_sends_base64_content_the_branch_and_no_sha()
    {
        var handler = new StubHandler(_ => Json(HttpStatusCode.Created, new { content = new { sha = "newsha" } }));

        var sha = await Store(handler).PutAsync("src/content/blog/new.md", "---\ntitle: Ünï\n---\n", null, "Add blog/new");

        sha.Should().Be("newsha");
        var call = handler.Calls.Single();
        call.Request.Method.Should().Be(HttpMethod.Put);

        using var body = JsonDocument.Parse(call.Body);
        body.RootElement.GetProperty("message").GetString().Should().Be("Add blog/new");
        body.RootElement.GetProperty("branch").GetString().Should().Be("main");
        body.RootElement.TryGetProperty("sha", out _).Should().BeFalse("a new file has no previous version");
        Encoding.UTF8.GetString(Convert.FromBase64String(body.RootElement.GetProperty("content").GetString()!)).Should().Be("---\ntitle: Ünï\n---\n");
    }

    [Fact]
    public async Task Updating_a_file_sends_the_version_it_replaces()
    {
        var handler = new StubHandler(_ => Json(HttpStatusCode.OK, new { content = new { sha = "v2" } }));

        await Store(handler).PutAsync("src/content/blog/x.md", "x", "v1", "Update");

        using var body = JsonDocument.Parse(handler.Calls.Single().Body);
        body.RootElement.GetProperty("sha").GetString().Should().Be("v1");
    }

    [Fact]
    public async Task Deleting_sends_the_version_and_message()
    {
        var handler = new StubHandler(_ => Json(HttpStatusCode.OK, new { commit = new { sha = "c" } }));

        await Store(handler).DeleteAsync("src/content/blog/x.md", "v1", "Delete blog/x");

        var call = handler.Calls.Single();
        call.Request.Method.Should().Be(HttpMethod.Delete);
        using var body = JsonDocument.Parse(call.Body);
        body.RootElement.GetProperty("sha").GetString().Should().Be("v1");
        body.RootElement.GetProperty("message").GetString().Should().Be("Delete blog/x");
        body.RootElement.GetProperty("branch").GetString().Should().Be("main");
    }

    // ---- Failures --------------------------------------------------------------------------

    [Theory]
    [InlineData(HttpStatusCode.Conflict)]
    [InlineData(HttpStatusCode.UnprocessableEntity)]
    public async Task A_stale_version_or_duplicate_becomes_a_conflict(HttpStatusCode status) =>
        (await Record.ExceptionAsync(() => Store(new StubHandler(_ => Json(status, new { message = "sha does not match" })))
            .PutAsync("src/content/blog/x.md", "x", "old", "m"))).Should().BeOfType<ConflictException>();

    [Fact]
    public async Task A_rejected_token_tells_the_admin_to_replace_it_without_echoing_it()
    {
        var failure = await Record.ExceptionAsync(() => Store(new StubHandler(_ => Json(HttpStatusCode.Unauthorized, new { message = "Bad credentials" })))
            .GetAsync("src/content/blog/x.md"));

        failure.Should().BeOfType<ExternalServiceException>().Which.Message.Should().Contain("token").And.NotContain(Token);
    }

    [Fact]
    public async Task Missing_permissions_and_rate_limits_are_told_apart()
    {
        var forbidden = await Record.ExceptionAsync(() => Store(new StubHandler(_ => Json(HttpStatusCode.Forbidden, new { message = "Resource not accessible by personal access token" })))
            .PutAsync("p", "c", null, "m"));
        var limited = await Record.ExceptionAsync(() => Store(new StubHandler(_ => Json(HttpStatusCode.Forbidden, new { message = "API rate limit exceeded for user" })))
            .PutAsync("p", "c", null, "m"));

        forbidden!.Message.Should().Contain("Contents read and write");
        limited!.Message.Should().Contain("rate limit");
    }

    [Fact]
    public async Task A_github_outage_is_reported_as_an_upstream_problem() =>
        (await Record.ExceptionAsync(() => Store(new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.BadGateway)))
            .GetAsync("src/content/blog/x.md"))).Should().BeOfType<ExternalServiceException>();

    [Fact]
    public async Task A_network_failure_is_reported_without_leaking_details()
    {
        var failure = await Record.ExceptionAsync(() => Store(new ThrowingHandler()).GetAsync("src/content/blog/x.md"));

        failure.Should().BeOfType<ExternalServiceException>().Which.Message.Should().Be("Could not reach GitHub. Try again in a moment.");
    }

    [Fact]
    public async Task A_missing_file_on_delete_is_not_found() =>
        (await Record.ExceptionAsync(() => Store(new StubHandler(_ => Json(HttpStatusCode.NotFound, new { message = "Not Found" })))
            .DeleteAsync("src/content/blog/x.md", "v1", "m"))).Should().BeOfType<NotFoundException>();

    // ---- Path safety -----------------------------------------------------------------------

    [Theory]
    [InlineData("src/content/../../.github/workflows/deploy.yml")]
    [InlineData("../secrets")]
    [InlineData("src/./content")]
    public async Task Paths_that_climb_out_of_the_content_folder_are_refused_before_any_request(string path)
    {
        var handler = new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.OK));

        var failure = await Record.ExceptionAsync(() => Store(handler).PutAsync(path, "x", null, "m"));

        failure.Should().BeOfType<DomainException>();
        handler.Calls.Should().BeEmpty();
    }

    [Fact]
    public async Task Each_path_segment_is_url_encoded_so_nothing_can_be_smuggled_into_the_query()
    {
        var handler = new StubHandler(_ => Json(HttpStatusCode.NotFound, new { message = "Not Found" }));

        await Store(handler).GetAsync("src/content/blog/odd name?x=1#frag.md");

        var url = handler.Calls.Single().Request.RequestUri!.AbsoluteUri;
        url.Should().Contain("odd%20name%3Fx%3D1%23frag.md").And.EndWith("?ref=main");
    }

    [Fact]
    public async Task The_token_is_never_placed_in_the_url_or_body()
    {
        var handler = new StubHandler(_ => Json(HttpStatusCode.OK, new { content = new { sha = "s" } }));

        await Store(handler).PutAsync("src/content/blog/x.md", "content", null, "message");

        var call = handler.Calls.Single();
        call.Request.RequestUri!.ToString().Should().NotContain(Token);
        call.Body.Should().NotContain(Token);
    }
}
