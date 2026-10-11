using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Articles.Application.Subscriptions;
using Articles.Tests.Subscriptions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Xunit;

namespace Articles.Tests.Api;

/// <summary>Email subscription through the real HTTP pipeline, with the mail service replaced by one that records what it is sent.</summary>
public class SubscribePipelineTests : IClassFixture<AdminApiFactory>
{
    private static int _ipCounter = 200;
    private readonly AdminApiFactory _factory;
    private readonly FakeEmailSender _email = new();
    private readonly HttpClient _client;
    private readonly HttpClient _unconfigured;

    public SubscribePipelineTests(AdminApiFactory factory)
    {
        _factory = factory;
        _unconfigured = factory.CreateClient();
        _client = factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<IEmailSender>();
            services.AddSingleton<IEmailSender>(_email);
        })).CreateClient();
    }

    private static string NewIp() => $"198.51.100.{Interlocked.Increment(ref _ipCounter) % 250}";

    private Task<HttpResponseMessage> Send(HttpMethod method, string path, string? token = null, object? body = null, string? ip = null, HttpClient? client = null)
    {
        var request = new HttpRequestMessage(method, path);
        request.Headers.Add("CF-Connecting-IP", ip ?? NewIp());
        if (token is not null) request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        if (body is not null) request.Content = JsonContent.Create(body);
        return (client ?? _client).SendAsync(request);
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

    private static string NewAddress() => $"reader-{Guid.NewGuid():N}@example.com";

    private async Task<string> ConfirmedAsync()
    {
        var address = NewAddress();
        await Send(HttpMethod.Post, "/api/v1/subscribers", body: new { email = address });
        (await Send(HttpMethod.Post, "/api/v1/subscribers/confirm", body: new { token = _email.TokenFor(address) })).StatusCode.Should().Be(HttpStatusCode.NoContent);
        return address;
    }

    // ---- Before email is set up ---------------------------------------------------------------

    [Fact]
    public async Task Until_email_is_set_up_the_site_says_so_and_subscribing_is_refused()
    {
        var site = await Json(await Send(HttpMethod.Get, "/api/v1/site", client: _unconfigured));
        var attempt = await Send(HttpMethod.Post, "/api/v1/subscribers", body: new { email = NewAddress() }, client: _unconfigured);

        site.GetProperty("subscribeEnabled").GetBoolean().Should().BeFalse();
        attempt.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Once_email_is_set_up_the_site_offers_subscribing()
    {
        (await Json(await Send(HttpMethod.Get, "/api/v1/site"))).GetProperty("subscribeEnabled").GetBoolean().Should().BeTrue();
    }

    // ---- A reader subscribes ------------------------------------------------------------------

    [Fact]
    public async Task A_reader_subscribes_confirms_and_can_leave_without_any_account()
    {
        var address = NewAddress();

        var asked = await Send(HttpMethod.Post, "/api/v1/subscribers", body: new { email = address });
        asked.StatusCode.Should().Be(HttpStatusCode.Accepted);
        _email.Sent.Should().Contain(m => m.To == address && m.Subject.Contains("Confirm"));

        var confirmed = await Send(HttpMethod.Post, "/api/v1/subscribers/confirm", body: new { token = _email.TokenFor(address) });
        confirmed.StatusCode.Should().Be(HttpStatusCode.NoContent);

        var token = await AdminTokenAsync();
        var list = (await Json(await Send(HttpMethod.Get, "/api/v1/admin/subscribers?pageSize=100", token))).GetProperty("items").EnumerateArray().ToList();
        var mine = list.Single(s => s.GetProperty("email").GetString() == address);
        mine.GetProperty("status").GetString().Should().Be("Confirmed");
    }

    [Fact]
    public async Task The_answer_is_identical_for_a_new_address_and_one_already_subscribed()
    {
        var existing = await ConfirmedAsync();

        var known = await Send(HttpMethod.Post, "/api/v1/subscribers", body: new { email = existing });
        var fresh = await Send(HttpMethod.Post, "/api/v1/subscribers", body: new { email = NewAddress() });

        known.StatusCode.Should().Be(fresh.StatusCode).And.Be(HttpStatusCode.Accepted);
        (await known.Content.ReadAsStringAsync()).Should().Be(await fresh.Content.ReadAsStringAsync());
    }

    [Theory]
    [InlineData("")]
    [InlineData("not-an-address")]
    [InlineData("Name <reader@example.com>")]
    public async Task An_address_that_is_not_one_is_refused(string email)
    {
        (await Send(HttpMethod.Post, "/api/v1/subscribers", body: new { email })).StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Theory]
    [InlineData("confirm")]
    [InlineData("unsubscribe")]
    public async Task A_made_up_link_does_nothing(string action)
    {
        (await Send(HttpMethod.Post, $"/api/v1/subscribers/{action}", body: new { token = "AAAAAAAAAAAAAAAAAAAAAA.BBBBBBBBBBBBBBBBBBBBBB" })).StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task One_address_cannot_request_confirmation_emails_without_limit()
    {
        var ip = NewIp();
        var statuses = new List<HttpStatusCode>();

        for (var i = 0; i < 7; i++)
            statuses.Add((await Send(HttpMethod.Post, "/api/v1/subscribers", body: new { email = NewAddress() }, ip: ip)).StatusCode);

        statuses.Take(5).Should().OnlyContain(s => s == HttpStatusCode.Accepted);
        statuses.Skip(5).Should().OnlyContain(s => s == HttpStatusCode.TooManyRequests);
    }

    // ---- The owner ----------------------------------------------------------------------------

    [Fact]
    public async Task Only_an_admin_session_can_see_subscribers_or_send_to_them()
    {
        var reader = await ReaderTokenAsync();
        var attempts = new (HttpMethod Method, string Path)[]
        {
            (HttpMethod.Get, "/api/v1/admin/subscribers"),
            (HttpMethod.Get, "/api/v1/admin/subscribers/summary"),
            (HttpMethod.Delete, $"/api/v1/admin/subscribers/{Guid.NewGuid()}"),
            (HttpMethod.Get, $"/api/v1/admin/posts/{Guid.NewGuid()}/notify"),
            (HttpMethod.Post, $"/api/v1/admin/posts/{Guid.NewGuid()}/notify"),
        };

        foreach (var (method, path) in attempts)
        {
            (await Send(method, path)).StatusCode.Should().Be(HttpStatusCode.Unauthorized, $"{method} {path} with no session");
            (await Send(method, path, reader)).StatusCode.Should().Be(HttpStatusCode.Forbidden, $"{method} {path} as an ordinary account");
        }
    }

    [Fact]
    public async Task The_owner_announces_a_post_once_and_the_unsubscribe_link_in_it_works()
    {
        var address = await ConfirmedAsync();
        var token = await AdminTokenAsync();
        var created = await Json(await Send(HttpMethod.Post, "/api/v1/posts", token,
            new { title = "Announced to subscribers", subtitle = "Worth reading.", contentJson = TestDatabase.Document("Body."), coverImageUrl = (string?)null, tags = Array.Empty<string>() }));
        var postId = created.GetProperty("id").GetString();

        // A draft cannot be announced.
        (await Send(HttpMethod.Post, $"/api/v1/admin/posts/{postId}/notify", token)).StatusCode.Should().Be(HttpStatusCode.BadRequest);

        var published = await Json(await Send(HttpMethod.Post, $"/api/v1/posts/{postId}/status", token, new { status = "Published" }));
        var slug = published.GetProperty("slug").GetString();
        (await Json(await Send(HttpMethod.Get, $"/api/v1/admin/posts/{postId}/notify", token))).GetProperty("waiting").GetInt32().Should().BeGreaterThan(0);

        var result = await Json(await Send(HttpMethod.Post, $"/api/v1/admin/posts/{postId}/notify", token));
        result.GetProperty("sent").GetInt32().Should().BeGreaterThan(0);
        result.GetProperty("remaining").GetInt32().Should().Be(0);

        var message = _email.Sent.Last(m => m.To == address);
        message.Subject.Should().Be("New article: Announced to subscribers");
        message.Text.Should().Contain($"/read/{slug}").And.Contain("Worth reading.");

        // A second press reaches nobody.
        (await Send(HttpMethod.Post, $"/api/v1/admin/posts/{postId}/notify", token)).StatusCode.Should().Be(HttpStatusCode.Conflict);
        _email.Sent.Count(m => m.To == address && m.Subject.Contains("Announced")).Should().Be(1);

        // The post now shows when it was announced.
        var listed = (await Json(await Send(HttpMethod.Get, "/api/v1/admin/posts?pageSize=100", token))).GetProperty("items").EnumerateArray().Single(p => p.GetProperty("id").GetString() == postId);
        listed.GetProperty("notifiedAt").ValueKind.Should().Be(JsonValueKind.String);

        // And the reader can leave using the link they were sent.
        (await Send(HttpMethod.Post, "/api/v1/subscribers/unsubscribe", body: new { token = _email.TokenFor(address, "unsubscribe?token=") })).StatusCode.Should().Be(HttpStatusCode.NoContent);
        var after = (await Json(await Send(HttpMethod.Get, "/api/v1/admin/subscribers?pageSize=100", token))).GetProperty("items").EnumerateArray().Single(s => s.GetProperty("email").GetString() == address);
        after.GetProperty("status").GetString().Should().Be("Unsubscribed");
    }

    [Fact]
    public async Task The_owner_can_remove_an_address()
    {
        var address = await ConfirmedAsync();
        var token = await AdminTokenAsync();
        var id = (await Json(await Send(HttpMethod.Get, "/api/v1/admin/subscribers?pageSize=100", token))).GetProperty("items").EnumerateArray()
            .Single(s => s.GetProperty("email").GetString() == address).GetProperty("id").GetString();

        (await Send(HttpMethod.Delete, $"/api/v1/admin/subscribers/{id}", token)).StatusCode.Should().Be(HttpStatusCode.NoContent);

        (await Json(await Send(HttpMethod.Get, "/api/v1/admin/subscribers?pageSize=100", token))).GetProperty("items").EnumerateArray()
            .Should().NotContain(s => s.GetProperty("email").GetString() == address);
        (await Send(HttpMethod.Delete, $"/api/v1/admin/subscribers/{id}", token)).StatusCode.Should().Be(HttpStatusCode.NotFound);
    }
}
