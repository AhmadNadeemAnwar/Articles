using System.Net;
using System.Text;
using System.Text.Json;
using Articles.Application.Site;
using Articles.Domain.Exceptions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Articles.Infrastructure.Images;

/// <summary>
/// Asks a text model on Cloudflare Workers AI, with the same account and token as picture generation
/// and on the same free daily allowance (a short answer costs very little of it).
/// </summary>
public sealed class CloudflareThemeAi : IThemeAi
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(40);

    private readonly HttpClient _http;
    private readonly ImageGenerationOptions _options;
    private readonly ILogger<CloudflareThemeAi> _logger;

    public CloudflareThemeAi(HttpClient http, IOptions<ImageGenerationOptions> options, ILogger<CloudflareThemeAi> logger)
    {
        _http = http;
        _options = options.Value;
        _logger = logger;
    }

    public bool IsEnabled => !string.IsNullOrWhiteSpace(_options.AccountId) && !string.IsNullOrWhiteSpace(_options.ApiToken);

    private string AccountId => _options.AccountId.Trim();
    private string ApiToken => _options.ApiToken.Trim();

    public async Task<string> AskAsync(string instruction, string request, CancellationToken ct = default)
    {
        if (!CloudflareImageGenerator.IsValidAccountId(AccountId))
            throw new DomainException("The Cloudflare account ID on the server looks wrong. Check ImageGeneration__AccountId on Render.");

        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(Timeout);

            var url = $"{_options.BaseUrl.TrimEnd('/')}/client/v4/accounts/{Uri.EscapeDataString(AccountId)}/ai/run/{_options.TextModel}";
            var payload = new
            {
                messages = new object[] { new { role = "system", content = instruction }, new { role = "user", content = request } },
                max_tokens = 200,
                temperature = 1.0,
            };
            using var message = new HttpRequestMessage(HttpMethod.Post, url) { Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json") };
            message.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", ApiToken);

            using var response = await _http.SendAsync(message, timeout.Token);
            var body = await response.Content.ReadAsStringAsync(timeout.Token);

            if (!response.IsSuccessStatusCode) throw Failure(response.StatusCode, body);
            return Parse(body);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or OperationCanceledException or JsonException)
        {
            if (ct.IsCancellationRequested) throw;
            // The message never includes the request, so the token cannot end up in a log.
            _logger.LogWarning("Asking the AI for colours failed to run: {Reason}", ex.GetType().Name);
            throw new DomainException("The AI service could not be reached. Try again in a moment.");
        }
    }

    internal static string Parse(string json)
    {
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        var holder = root.TryGetProperty("result", out var result) && result.ValueKind == JsonValueKind.Object ? result : root;
        return holder.TryGetProperty("response", out var response) && response.ValueKind == JsonValueKind.String
            ? response.GetString() ?? string.Empty
            : throw new DomainException("The AI sent back no answer. Try again.");
    }

    private DomainException Failure(HttpStatusCode status, string body)
    {
        var allowanceSpent = status == HttpStatusCode.TooManyRequests || body.Contains("\"code\":4006", StringComparison.Ordinal) || body.Contains("daily free allocation", StringComparison.OrdinalIgnoreCase);
        if (allowanceSpent)
            return new DomainException("Cloudflare's free allowance for today is used up. Try again tomorrow; it resets at midnight UTC.");

        if (status is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
        {
            _logger.LogError("Cloudflare refused the AI request ({Status}). Check ImageGeneration__ApiToken and its Workers AI permission.", (int)status);
            return new DomainException("Cloudflare refused the request. The API token may be wrong or missing the Workers AI permission. See ADMIN.md.");
        }

        var reason = CloudflareImageGenerator.Reason(body);
        _logger.LogWarning("Cloudflare AI request failed with {Status}: {Reason}", (int)status, reason);
        return new DomainException($"The AI could not answer (Cloudflare answered {(int)status}{(reason.Length > 0 ? $": {reason}" : "")}). Try again.");
    }
}
