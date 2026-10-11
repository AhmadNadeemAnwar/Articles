using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Articles.Application.Site;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Xunit;

namespace Articles.Tests.Api;

/// <summary>"Amaze with AI" through the real HTTP pipeline, with the AI replaced.</summary>
public class ThemeSuggestionPipelineTests : IClassFixture<AdminApiFactory>
{
    private static int _ipCounter = 120;
    private readonly AdminApiFactory _factory;
    private readonly HttpClient _client;

    private sealed class FakeAi : IThemeAi
    {
        public bool IsEnabled => true;
        public Task<string> AskAsync(string instruction, string request, CancellationToken ct = default) =>
            Task.FromResult("{\"name\":\"Sea Glass\",\"header\":\"#0f4c5c\",\"accent\":\"#e36414\",\"background\":\"#f4f9f9\"}");
    }

    public ThemeSuggestionPipelineTests(AdminApiFactory factory)
    {
        _factory = factory;
        _client = factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<IThemeAi>();
            services.AddSingleton<IThemeAi>(new FakeAi());
        })).CreateClient();
    }

    private Task<HttpResponseMessage> Send(HttpMethod method, string path, string? token = null)
    {
        var request = new HttpRequestMessage(method, path);
        request.Headers.Add("CF-Connecting-IP", $"203.0.113.{Interlocked.Increment(ref _ipCounter) % 250}");
        if (token is not null) request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return _client.SendAsync(request);
    }

    private static async Task<JsonElement> Json(HttpResponseMessage response) =>
        JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement.Clone();

    private async Task<string> TokenAsync(bool admin)
    {
        await _factory.EnsureUsersAsync();
        var request = new HttpRequestMessage(HttpMethod.Post, admin ? "/api/v1/admin/auth/login" : "/api/v1/auth/login");
        request.Headers.Add("CF-Connecting-IP", $"203.0.113.{Interlocked.Increment(ref _ipCounter) % 250}");
        request.Content = JsonContent.Create(admin
            ? new { email = AdminApiFactory.AdminEmail, code = _factory.FreshCode() }
            : (object)new { email = AdminApiFactory.ReaderEmail, password = AdminApiFactory.Password });
        return (await Json(await _client.SendAsync(request))).GetProperty("token").GetString()!;
    }

    [Fact]
    public async Task Only_an_admin_session_can_ask_the_ai()
    {
        (await Send(HttpMethod.Post, "/api/v1/admin/settings/ai-colours")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await Send(HttpMethod.Get, "/api/v1/admin/settings/ai-colours")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await Send(HttpMethod.Post, "/api/v1/admin/settings/ai-colours", await TokenAsync(admin: false))).StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task The_admin_gets_three_colours_back_and_the_theme_is_not_changed()
    {
        var token = await TokenAsync(admin: true);
        var before = await Json(await Send(HttpMethod.Get, "/api/v1/site"));

        var response = await Send(HttpMethod.Post, "/api/v1/admin/settings/ai-colours", token);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var json = await Json(response);
        json.GetProperty("colors").GetProperty("main").GetString().Should().Be("#0f4c5c");
        json.GetProperty("colors").GetProperty("accent").GetString().Should().Be("#e36414");
        json.GetProperty("colors").GetProperty("background").GetString().Should().Be("#f4f9f9");
        json.GetProperty("name").GetString().Should().Be("Sea Glass");
        json.GetProperty("dailyLimit").GetInt32().Should().Be(20);
        (await Json(await Send(HttpMethod.Get, "/api/v1/site"))).GetProperty("theme").GetString().Should().Be(before.GetProperty("theme").GetString());
    }

    [Fact]
    public async Task The_status_says_whether_the_ai_is_set_up_and_how_many_asks_are_left()
    {
        var json = await Json(await Send(HttpMethod.Get, "/api/v1/admin/settings/ai-colours", await TokenAsync(admin: true)));

        json.GetProperty("enabled").GetBoolean().Should().BeTrue();
        json.GetProperty("dailyLimit").GetInt32().Should().Be(20);
    }

    [Fact]
    public async Task The_real_ai_is_registered_and_off_until_configured()
    {
        using var scope = _factory.Services.CreateScope();

        scope.ServiceProvider.GetRequiredService<IThemeAi>().IsEnabled.Should().BeFalse();
    }
}
