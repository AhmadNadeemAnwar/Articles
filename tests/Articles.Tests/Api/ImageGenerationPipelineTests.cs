using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Articles.Application.Images;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Xunit;

namespace Articles.Tests.Api;

/// <summary>Picture generation through the real HTTP pipeline, with the outside service replaced.</summary>
public class ImageGenerationPipelineTests : IClassFixture<AdminApiFactory>
{
    private static readonly byte[] Jpeg = [0xFF, 0xD8, 0xFF, 0xE0, 9, 8, 7];
    private static int _ipCounter = 60;

    private readonly AdminApiFactory _factory;
    private readonly FakeGenerator _generator = new();
    private readonly HttpClient _client;

    private sealed class FakeGenerator : IImageGenerator
    {
        public bool IsEnabled { get; set; } = true;
        public List<string> Prompts { get; } = [];

        public Task<byte[]> GenerateAsync(string prompt, CancellationToken ct = default)
        {
            Prompts.Add(prompt);
            return Task.FromResult(Jpeg);
        }
    }

    public ImageGenerationPipelineTests(AdminApiFactory factory)
    {
        _factory = factory;
        _client = factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<IImageGenerator>();
            services.AddSingleton<IImageGenerator>(_generator);
        })).CreateClient();
    }

    private static string NewIp() => $"192.0.2.{Interlocked.Increment(ref _ipCounter) % 250}";

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
    public async Task Someone_not_signed_in_cannot_use_it()
    {
        (await Send(HttpMethod.Get, "/api/v1/admin/images/generation")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await Send(HttpMethod.Post, "/api/v1/admin/images/generate", body: new { prompt = "a lighthouse" })).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        _generator.Prompts.Should().BeEmpty();
    }

    [Fact]
    public async Task An_ordinary_reader_account_cannot_use_it()
    {
        var response = await Send(HttpMethod.Post, "/api/v1/admin/images/generate", await ReaderTokenAsync(), new { prompt = "a lighthouse" });

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        _generator.Prompts.Should().BeEmpty();
    }

    [Fact]
    public async Task An_admin_gets_a_picture_back_as_base64_with_the_count()
    {
        var token = await AdminTokenAsync();

        var response = await Send(HttpMethod.Post, "/api/v1/admin/images/generate", token, new { prompt = "a lighthouse at dusk" });
        var json = await Json(response);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        Convert.FromBase64String(json.GetProperty("imageBase64").GetString()!).Should().Equal(Jpeg);
        json.GetProperty("contentType").GetString().Should().Be("image/jpeg");
        json.GetProperty("dailyLimit").GetInt32().Should().Be(20);
        json.GetProperty("usedToday").GetInt32().Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task The_status_says_whether_generation_is_set_up()
    {
        var token = await AdminTokenAsync();

        var on = await Json(await Send(HttpMethod.Get, "/api/v1/admin/images/generation", token));
        _generator.IsEnabled = false;
        var off = await Json(await Send(HttpMethod.Get, "/api/v1/admin/images/generation", token));

        on.GetProperty("enabled").GetBoolean().Should().BeTrue();
        on.GetProperty("dailyLimit").GetInt32().Should().Be(20);
        off.GetProperty("enabled").GetBoolean().Should().BeFalse();
    }

    [Fact]
    public async Task A_missing_description_is_a_plain_bad_request()
    {
        var response = await Send(HttpMethod.Post, "/api/v1/admin/images/generate", await AdminTokenAsync(), new { prompt = "" });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task The_real_generator_is_registered_and_off_until_configured()
    {
        using var scope = _factory.Services.CreateScope();

        var generator = scope.ServiceProvider.GetRequiredService<IImageGenerator>();

        generator.GetType().Name.Should().Be("CloudflareImageGenerator");
        generator.IsEnabled.Should().BeFalse();
    }
}
