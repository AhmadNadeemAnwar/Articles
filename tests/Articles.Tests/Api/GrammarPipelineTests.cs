using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Articles.Application.Grammar;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Xunit;

namespace Articles.Tests.Api;

/// <summary>The grammar endpoint through the real HTTP pipeline, with the outside service replaced.</summary>
public class GrammarPipelineTests : IClassFixture<AdminApiFactory>
{
    private static int _ipCounter = 10;
    private readonly AdminApiFactory _factory;
    private readonly FakeGrammarChecker _checker = new();
    private readonly HttpClient _client;

    private sealed class FakeGrammarChecker : IGrammarChecker
    {
        public List<string> Seen { get; } = [];

        public Task<GrammarResult> CheckAsync(string text, CancellationToken ct = default)
        {
            Seen.Add(text);
            return Task.FromResult(new GrammarResult([new GrammarMatch(0, 3, "Possible typo", "Typo", ["The"])]));
        }
    }

    public GrammarPipelineTests(AdminApiFactory factory)
    {
        _factory = factory;
        _client = factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<IGrammarChecker>();
            services.AddSingleton<IGrammarChecker>(_checker);
        })).CreateClient();
    }

    private static string NewIp() => $"203.0.113.{Interlocked.Increment(ref _ipCounter) % 250}";

    private Task<HttpResponseMessage> Send(HttpMethod method, string path, string? token = null, object? body = null, string? ip = null)
    {
        var request = new HttpRequestMessage(method, path);
        request.Headers.Add("CF-Connecting-IP", ip ?? NewIp());
        if (token is not null) request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        if (body is not null) request.Content = JsonContent.Create(body);
        return _client.SendAsync(request);
    }

    private async Task<string> AdminTokenAsync()
    {
        await _factory.EnsureUsersAsync();
        var response = await Send(HttpMethod.Post, "/api/v1/admin/auth/login", body: new { email = AdminApiFactory.AdminEmail, code = _factory.FreshCode() });
        return JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement.GetProperty("token").GetString()!;
    }

    private async Task<string> ReaderTokenAsync()
    {
        await _factory.EnsureUsersAsync();
        var response = await Send(HttpMethod.Post, "/api/v1/auth/login", body: new { email = AdminApiFactory.ReaderEmail, password = AdminApiFactory.Password });
        return JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement.GetProperty("token").GetString()!;
    }

    [Fact]
    public void The_real_checker_is_registered_when_nothing_replaces_it()
    {
        using var scope = _factory.Services.CreateScope();

        scope.ServiceProvider.GetRequiredService<IGrammarChecker>().GetType().Name.Should().Be("LanguageToolChecker");
    }

    [Fact]
    public async Task Someone_not_signed_in_cannot_check_text()
    {
        (await Send(HttpMethod.Post, "/api/v1/admin/grammar/check", body: new { text = "Teh cat" })).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        _checker.Seen.Should().BeEmpty();
    }

    [Fact]
    public async Task An_ordinary_reader_account_cannot_check_text()
    {
        var response = await Send(HttpMethod.Post, "/api/v1/admin/grammar/check", await ReaderTokenAsync(), new { text = "Teh cat" });

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        _checker.Seen.Should().BeEmpty();
    }

    [Fact]
    public async Task An_admin_gets_suggestions_back()
    {
        var response = await Send(HttpMethod.Post, "/api/v1/admin/grammar/check", await AdminTokenAsync(), new { text = "Teh cat" });
        var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var match = json.GetProperty("matches")[0];
        match.GetProperty("offset").GetInt32().Should().Be(0);
        match.GetProperty("replacements")[0].GetString().Should().Be("The");
        _checker.Seen.Should().Equal("Teh cat");
    }

    [Fact]
    public async Task Text_over_the_limit_is_refused_without_calling_the_service()
    {
        var tooLong = new string('a', IGrammarChecker.MaxCharacters + 1);

        var response = await Send(HttpMethod.Post, "/api/v1/admin/grammar/check", await AdminTokenAsync(), new { text = tooLong });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        _checker.Seen.Should().BeEmpty();
    }

    [Fact]
    public async Task Checks_are_held_below_the_free_services_own_limit()
    {
        var token = await AdminTokenAsync();
        var ip = NewIp();
        var statuses = new List<HttpStatusCode>();

        for (var i = 0; i < 17; i++)
            statuses.Add((await Send(HttpMethod.Post, "/api/v1/admin/grammar/check", token, new { text = "Hi" }, ip)).StatusCode);

        statuses.Take(15).Should().OnlyContain(s => s == HttpStatusCode.OK);
        statuses.Skip(15).Should().OnlyContain(s => s == HttpStatusCode.TooManyRequests);
    }
}
