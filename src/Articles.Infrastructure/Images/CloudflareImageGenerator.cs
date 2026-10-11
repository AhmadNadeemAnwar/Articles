using System.Net;
using System.Text;
using System.Text.Json;
using Articles.Application.Images;
using Articles.Domain.Exceptions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Articles.Infrastructure.Images;

public sealed class ImageGenerationOptions
{
    public const string SectionName = "ImageGeneration";

    /// <summary>Cloudflare account id. Set on the host (ImageGeneration__AccountId). Empty switches generation off.</summary>
    public string AccountId { get; set; } = string.Empty;

    /// <summary>A Cloudflare API token that can use Workers AI. Set on the host, never in the repository.</summary>
    public string ApiToken { get; set; } = string.Empty;

    public string Model { get; set; } = "@cf/black-forest-labs/flux-1-schnell";

    public string BaseUrl { get; set; } = "https://api.cloudflare.com";

    /// <summary>Diffusion steps. The model's maximum is 8; more steps cost more of the free allowance.</summary>
    public int Steps { get; set; } = 4;
}

/// <summary>Makes pictures with Cloudflare Workers AI, on the free daily allowance.</summary>
public sealed class CloudflareImageGenerator : IImageGenerator
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(60);

    private readonly HttpClient _http;
    private readonly ImageGenerationOptions _options;
    private readonly ILogger<CloudflareImageGenerator> _logger;

    public CloudflareImageGenerator(HttpClient http, IOptions<ImageGenerationOptions> options, ILogger<CloudflareImageGenerator> logger)
    {
        _http = http;
        _options = options.Value;
        _logger = logger;
    }

    public bool IsEnabled => !string.IsNullOrWhiteSpace(_options.AccountId) && !string.IsNullOrWhiteSpace(_options.ApiToken);

    // Pasting into a settings box often brings a space or line break along; neither belongs in a URL or a header.
    private string AccountId => _options.AccountId.Trim();
    private string ApiToken => _options.ApiToken.Trim();

    /// <summary>A Cloudflare account id is 32 hexadecimal characters.</summary>
    internal static bool IsValidAccountId(string value) => value.Length == 32 && value.All(Uri.IsHexDigit);

    public async Task<byte[]> GenerateAsync(string prompt, CancellationToken ct = default)
    {
        if (!IsValidAccountId(AccountId))
            throw new DomainException($"The Cloudflare account ID on the server looks wrong: it should be 32 letters and digits, and this one has {AccountId.Length}. Copy it again from Workers & Pages and save ImageGeneration__AccountId on Render.");

        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(Timeout);

            var url = $"{_options.BaseUrl.TrimEnd('/')}/client/v4/accounts/{Uri.EscapeDataString(AccountId)}/ai/run/{_options.Model}";
            using var request = new HttpRequestMessage(HttpMethod.Post, url)
            {
                Content = new StringContent(JsonSerializer.Serialize(new { prompt, steps = Math.Clamp(_options.Steps, 1, 8) }), Encoding.UTF8, "application/json")
            };
            request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", ApiToken);

            using var response = await _http.SendAsync(request, timeout.Token);
            var body = await response.Content.ReadAsStringAsync(timeout.Token);

            if (!response.IsSuccessStatusCode) throw Failure(response.StatusCode, body);
            return Parse(body);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or OperationCanceledException or JsonException or FormatException)
        {
            if (ct.IsCancellationRequested) throw;
            // The message never includes the request, so the token cannot end up in a log.
            _logger.LogWarning("Picture generation failed to run: {Reason}", ex.GetType().Name);
            throw new DomainException("The picture service could not be reached. Try again in a moment.");
        }
    }

    internal static byte[] Parse(string json)
    {
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;

        // The REST API wraps the answer in "result"; some routes return the bare object.
        var holder = root.TryGetProperty("result", out var result) && result.ValueKind == JsonValueKind.Object ? result : root;
        if (!holder.TryGetProperty("image", out var image) || image.ValueKind != JsonValueKind.String)
            throw new DomainException("The picture service sent back no picture. Try again, or word the description differently.");

        return Convert.FromBase64String(image.GetString()!);
    }

    private DomainException Failure(HttpStatusCode status, string body)
    {
        // Cloudflare's code for "the day's free neurons are spent". It stops the call rather than billing.
        var allowanceSpent = status == HttpStatusCode.TooManyRequests || body.Contains("\"code\":4006", StringComparison.Ordinal) || body.Contains("daily free allocation", StringComparison.OrdinalIgnoreCase);
        if (allowanceSpent)
            return new DomainException("Cloudflare's free allowance for today is used up. Try again tomorrow; it resets at midnight UTC.");

        if (status is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
        {
            _logger.LogError("Cloudflare refused the picture request ({Status}). Check ImageGeneration__ApiToken and its Workers AI permission.", (int)status);
            return new DomainException("Cloudflare refused the request. The API token may be wrong or missing the Workers AI permission. See ADMIN.md.");
        }

        var reason = Reason(body);
        _logger.LogWarning("Cloudflare picture request failed with {Status}: {Reason}", (int)status, reason);
        return new DomainException($"The picture service could not make that picture (Cloudflare answered {(int)status}{(reason.Length > 0 ? $": {reason}" : "")}). Try again, or word the description differently.");
    }

    /// <summary>Cloudflare's own explanation, kept short. It describes the request, never the token.</summary>
    internal static string Reason(string body)
    {
        try
        {
            using var document = JsonDocument.Parse(body);
            if (document.RootElement.TryGetProperty("errors", out var errors) && errors.ValueKind == JsonValueKind.Array && errors.GetArrayLength() > 0
                && errors[0].TryGetProperty("message", out var message) && message.ValueKind == JsonValueKind.String)
            {
                var text = message.GetString() ?? string.Empty;
                return text.Length <= 200 ? text : text[..200];
            }
        }
        catch (JsonException)
        {
            // Not JSON: nothing useful to show.
        }

        return string.Empty;
    }
}
