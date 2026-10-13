using System.Net;
using System.Text;
using System.Text.Json;
using Articles.Application.Posts;
using Articles.Domain.Exceptions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Articles.Infrastructure.Images;

public sealed class GeminiOptions
{
    public const string SectionName = "Gemini";

    /// <summary>
    /// A key from Google AI Studio, on a project with no billing set up (the free tier). Set on the host (Gemini__ApiKey),
    /// never in the repository. Empty switches Gemini off, and the Cloudflare model is used instead.
    /// </summary>
    public string ApiKey { get; set; } = string.Empty;

    /// <summary>A Flash model, which is the kind Google offers free. Can be changed on the host without a new release.</summary>
    public string Model { get; set; } = "gemini-2.5-flash";

    public string BaseUrl { get; set; } = "https://generativelanguage.googleapis.com";
}

/// <summary>
/// Rewrites text with Google's Gemini on its free tier. Better English and Urdu than the small model on Cloudflare. Free-tier
/// content may be used by Google to improve its products, so only text the writer is about to publish should be sent.
/// </summary>
public sealed class GeminiTextModel : ITextModel
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(85);

    private readonly HttpClient _http;
    private readonly GeminiOptions _options;
    private readonly ILogger<GeminiTextModel> _logger;

    public GeminiTextModel(HttpClient http, IOptions<GeminiOptions> options, ILogger<GeminiTextModel> logger)
    {
        _http = http;
        _options = options.Value;
        _logger = logger;
    }

    public bool IsEnabled => !string.IsNullOrWhiteSpace(_options.ApiKey);

    // Pasting into a settings box often brings a space or line break along; neither belongs in a header.
    private string ApiKey => _options.ApiKey.Trim();
    private string Model => _options.Model.Trim();

    public async Task<string> CompleteAsync(string instruction, string request, int maxTokens, double temperature, CancellationToken ct = default)
    {
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(Timeout);

            var url = $"{_options.BaseUrl.TrimEnd('/')}/v1beta/models/{Uri.EscapeDataString(Model)}:generateContent";
            using var message = new HttpRequestMessage(HttpMethod.Post, url)
            {
                Content = new StringContent(JsonSerializer.Serialize(Payload(instruction, request, maxTokens, temperature)), Encoding.UTF8, "application/json"),
            };
            // The key goes in a header, not the address, so it cannot end up in a log line that records addresses.
            message.Headers.Add("x-goog-api-key", ApiKey);

            using var response = await _http.SendAsync(message, timeout.Token);
            var body = await response.Content.ReadAsStringAsync(timeout.Token);

            if (!response.IsSuccessStatusCode) throw Failure(response.StatusCode, body);
            return Parse(body);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or OperationCanceledException or JsonException)
        {
            if (ct.IsCancellationRequested) throw;
            _logger.LogWarning("Asking Gemini failed to run: {Reason}", ex.GetType().Name);
            throw new DomainException("Google's AI could not be reached. Try again in a moment.");
        }
    }

    private object Payload(string instruction, string request, int maxTokens, double temperature)
    {
        // Gemini 2.5 models "think" first, and that counts against the output limit, which would cut the rewrite short.
        // Rewriting does not need it, so it is turned off for them. Other models do not take the setting.
        object generation = Model.StartsWith("gemini-2.5", StringComparison.OrdinalIgnoreCase)
            ? new { temperature, maxOutputTokens = Math.Max(maxTokens, 1000), thinkingConfig = new { thinkingBudget = 0 } }
            : new { temperature, maxOutputTokens = Math.Max(maxTokens, 1000) };

        return new
        {
            systemInstruction = new { parts = new[] { new { text = instruction } } },
            contents = new[] { new { role = "user", parts = new[] { new { text = request } } } },
            generationConfig = generation,
        };
    }

    internal static string Parse(string json)
    {
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;

        if (root.TryGetProperty("promptFeedback", out var feedback) && feedback.TryGetProperty("blockReason", out _))
            throw new DomainException("Google's AI declined to rewrite this text. Try rewording it, or improve a smaller part.");

        if (root.TryGetProperty("candidates", out var candidates) && candidates.ValueKind == JsonValueKind.Array && candidates.GetArrayLength() > 0
            && candidates[0].TryGetProperty("content", out var content) && content.TryGetProperty("parts", out var parts) && parts.ValueKind == JsonValueKind.Array)
        {
            var text = string.Concat(parts.EnumerateArray()
                .Where(p => p.TryGetProperty("text", out var t) && t.ValueKind == JsonValueKind.String && !(p.TryGetProperty("thought", out var th) && th.ValueKind == JsonValueKind.True))
                .Select(p => p.GetProperty("text").GetString()));
            if (!string.IsNullOrWhiteSpace(text)) return text;
        }

        throw new DomainException("Google's AI sent back no answer. Try again.");
    }

    private DomainException Failure(HttpStatusCode status, string body)
    {
        if (status == HttpStatusCode.TooManyRequests)
            return new DomainException("Google's free allowance for the AI is used up for now. Try again in a minute, or tomorrow.");

        if (status is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
        {
            _logger.LogError("Google refused the AI request ({Status}). Check Gemini__ApiKey.", (int)status);
            return new DomainException("Google refused the request. The Gemini key on the server may be wrong, or not allowed to use this model.");
        }

        var reason = Reason(body);
        _logger.LogWarning("Gemini request failed with {Status}: {Reason}", (int)status, reason);
        return new DomainException($"Google's AI could not answer (it said {(int)status}{(reason.Length > 0 ? $": {reason}" : "")}). Try again.");
    }

    /// <summary>Google's own explanation, kept short. It describes the request, never the key.</summary>
    internal static string Reason(string body)
    {
        try
        {
            using var document = JsonDocument.Parse(body);
            if (document.RootElement.TryGetProperty("error", out var error) && error.TryGetProperty("message", out var message) && message.ValueKind == JsonValueKind.String)
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

/// <summary>
/// The model "Improve text with AI" uses: Gemini when its key is set, otherwise (or when Google cannot answer) the model on
/// Cloudflare. Both are free; with neither set up the feature is switched off.
/// </summary>
public sealed class PreferredTextModel : ITextModel
{
    private readonly GeminiTextModel _gemini;
    private readonly ITextModel _fallback;
    private readonly ILogger<PreferredTextModel> _logger;

    public PreferredTextModel(GeminiTextModel gemini, ITextModel fallback, ILogger<PreferredTextModel> logger)
    {
        _gemini = gemini;
        _fallback = fallback;
        _logger = logger;
    }

    public bool IsEnabled => _gemini.IsEnabled || _fallback.IsEnabled;

    public async Task<string> CompleteAsync(string instruction, string request, int maxTokens, double temperature, CancellationToken ct = default)
    {
        if (!_gemini.IsEnabled) return await _fallback.CompleteAsync(instruction, request, maxTokens, temperature, ct);

        try
        {
            return await _gemini.CompleteAsync(instruction, request, maxTokens, temperature, ct);
        }
        catch (DomainException ex) when (_fallback.IsEnabled)
        {
            // Google's free allowance can run out for a while, or it can be briefly down: the writer should still get an answer.
            _logger.LogWarning("Gemini did not answer ({Reason}); using the Cloudflare model instead.", ex.Message);
            return await _fallback.CompleteAsync(instruction, request, maxTokens, temperature, ct);
        }
    }
}
