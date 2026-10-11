using System.Text.Json;
using Articles.Application.Common;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Articles.Infrastructure.Security;

public sealed class TurnstileOptions
{
    public const string SectionName = "Turnstile";

    /// <summary>Secret from the Cloudflare Turnstile widget. Leave empty to disable the check.</summary>
    public string SecretKey { get; set; } = string.Empty;
}

/// <summary>Verifies a Cloudflare Turnstile token (free CAPTCHA alternative) on sign-up.</summary>
public sealed class TurnstileVerifier : ITurnstileVerifier
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(5);

    private readonly HttpClient _http;
    private readonly TurnstileOptions _options;
    private readonly ILogger<TurnstileVerifier> _logger;

    public TurnstileVerifier(HttpClient http, IOptions<TurnstileOptions> options, ILogger<TurnstileVerifier> logger)
    {
        _http = http;
        _options = options.Value;
        _logger = logger;
    }

    public bool IsEnabled => !string.IsNullOrWhiteSpace(_options.SecretKey);

    public async Task<bool> VerifyAsync(string? token, CancellationToken ct = default)
    {
        if (!IsEnabled) return true;
        if (string.IsNullOrWhiteSpace(token)) return false;

        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(Timeout);

            using var content = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["secret"] = _options.SecretKey,
                ["response"] = token
            });

            using var response = await _http.PostAsync("https://challenges.cloudflare.com/turnstile/v0/siteverify", content, timeout.Token);
            if (!response.IsSuccessStatusCode) return false;

            using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(timeout.Token));
            return json.RootElement.TryGetProperty("success", out var success) && success.ValueKind == JsonValueKind.True;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or OperationCanceledException or JsonException)
        {
            // Unlike the breached-password check this fails closed: when the bot check is switched
            // on, being unable to run it must not become a way around it.
            _logger.LogWarning(ex, "Turnstile verification failed to run");
            return false;
        }
    }
}
