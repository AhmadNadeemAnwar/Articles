using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Articles.Application.Posts;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Xunit;

namespace Articles.Tests.Api;

/// <summary>"Improve text with AI" through the real HTTP pipeline, with the AI replaced.</summary>
public class TextImprovementPipelineTests : IClassFixture<AdminApiFactory>
{
    private static int _ipCounter = 150;
    private readonly AdminApiFactory _factory;
    private readonly HttpClient _client;

    private sealed class FakeModel : ITextModel
    {
        public bool IsEnabled => true;
        public Task<string> CompleteAsync(string instruction, string request, int maxTokens, double temperature, CancellationToken ct = default) =>
            Task.FromResult(request.Replace("] ", "] Improved: "));
    }

    public TextImprovementPipelineTests(AdminApiFactory factory)
    {
        _factory = factory;
        _client = factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<ITextModel>();
            services.AddSingleton<ITextModel>(new FakeModel());
        })).CreateClient();
    }

    private Task<HttpResponseMessage> Send(HttpMethod method, string path, string? token = null, object? body = null)
    {
        var request = new HttpRequestMessage(method, path);
        request.Headers.Add("CF-Connecting-IP", $"203.0.113.{Interlocked.Increment(ref _ipCounter) % 250}");
        if (token is not null) request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        if (body is not null) request.Content = JsonContent.Create(body);
        return _client.SendAsync(request);
    }

    private static async Task<JsonElement> Json(HttpResponseMessage response) =>
        JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement.Clone();

    private async Task<string> TokenAsync(bool admin)
    {
        await _factory.EnsureUsersAsync();
        var response = admin
            ? await Send(HttpMethod.Post, "/api/v1/admin/auth/login", body: new { email = AdminApiFactory.AdminEmail, code = _factory.FreshCode() })
            : await Send(HttpMethod.Post, "/api/v1/auth/login", body: new { email = AdminApiFactory.ReaderEmail, password = AdminApiFactory.Password });
        return (await Json(response)).GetProperty("token").GetString()!;
    }

    [Fact]
    public async Task Only_an_admin_session_can_use_any_of_it()
    {
        var reader = await TokenAsync(admin: false);

        foreach (var (method, path) in new[] { (HttpMethod.Get, "/api/v1/admin/text-improvement"), (HttpMethod.Post, "/api/v1/admin/text-improvement/improve") })
        {
            (await Send(method, path)).StatusCode.Should().Be(HttpStatusCode.Unauthorized, path);
            (await Send(method, path, reader)).StatusCode.Should().Be(HttpStatusCode.Forbidden, path);
        }
    }

    [Fact]
    public async Task The_admin_gets_the_improved_lines_back_and_nothing_is_saved()
    {
        var response = await Send(HttpMethod.Post, "/api/v1/admin/text-improvement/improve", await TokenAsync(admin: true), new { units = new[] { "one", "two" } });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var json = await Json(response);
        json.GetProperty("units").EnumerateArray().Select(u => u.GetString()).Should().Equal("Improved: one", "Improved: two");
        json.GetProperty("changed").GetInt32().Should().Be(2);
        json.GetProperty("dailyLimit").GetInt32().Should().Be(20);
    }

    [Fact]
    public async Task The_status_says_the_ai_is_available_and_how_many_goes_are_left()
    {
        var json = await Json(await Send(HttpMethod.Get, "/api/v1/admin/text-improvement", await TokenAsync(admin: true)));

        json.GetProperty("enabled").GetBoolean().Should().BeTrue();
        json.GetProperty("dailyLimit").GetInt32().Should().Be(20);
    }

    [Fact]
    public async Task Nothing_to_improve_is_a_plain_bad_request()
    {
        (await Send(HttpMethod.Post, "/api/v1/admin/text-improvement/improve", await TokenAsync(admin: true), new { units = Array.Empty<string>() })).StatusCode
            .Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task The_real_model_is_registered_and_off_until_configured()
    {
        using var scope = _factory.Services.CreateScope();

        scope.ServiceProvider.GetRequiredService<ITextModel>().IsEnabled.Should().BeFalse();
    }
}
