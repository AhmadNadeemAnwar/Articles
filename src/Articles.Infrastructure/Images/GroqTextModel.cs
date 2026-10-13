using System.Net;
using System.Text;
using System.Text.Json;
using Articles.Application.Posts;
using Articles.Domain.Exceptions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Articles.Infrastructure.Images;

public sealed class GroqOptions
{
    public const string SectionName = "Groq";

    /// <summary>A key from console.groq.com, on the free plan (no card). Set on the host (Groq__ApiKey), never in the repository. Empty switches Groq off.</summary>
    public string ApiKey { get; set; } = string.Empty;

    /// <summary>Can be changed on the host without a new release, for example if Groq retires this one.</summary>
    public string Model { get; set; } = "llama-3.3-70b-versatile";

    /// <summary>
    /// Groq speaks the common "chat completions" format, so this same code also works with Mistral, GitHub Models and
    /// others by changing this address, the key and the model on the host.
    /// </summary>
    public string BaseUrl { get; set; } = "https://api.groq.com/openai/v1";
}

/// <summary>Rewrites text with a large open model on Groq's free plan: much better sentences than the small Cloudflare model.</summary>
public sealed class GroqTextModel : ITextModel
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(85);

    private readonly HttpClient _http;
    private readonly GroqOptions _options;
    private readonly ILogger<GroqTextModel> _logger;

    public GroqTextModel(HttpClient http, IOptions<GroqOptions> options, ILogger<GroqTextModel> logger)
    {
        _http = http;
        _options = options.Value;
        _logger = logger;
    }

    public bool IsEnabled => !string.IsNullOrWhiteSpace(_options.ApiKey);

    // Pasting into a settings box often brings a space or line break along; neither belongs in a header.
    private string ApiKey => _options.ApiKey.Trim();

    public async Task<string> CompleteAsync(string instruction, string request, int maxTokens, double temperature, CancellationToken ct = default)
    {
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(Timeout);

            var payload = new
            {
                model = _options.Model.Trim(),
                messages = new object[] { new { role = "system", content = instruction }, new { role = "user", content = request } },
                temperature,
                max_tokens = Math.Max(maxTokens, 1000),
            };
            using var message = new HttpRequestMessage(HttpMethod.Post, $"{_options.BaseUrl.TrimEnd('/')}/chat/completions")
            {
                Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json"),
            };
            message.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", ApiKey);

            using var response = await _http.SendAsync(message, timeout.Token);
            var body = await response.Content.ReadAsStringAsync(timeout.Token);

            if (!response.IsSuccessStatusCode) throw Failure(response.StatusCode, body);
            return Parse(body);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or OperationCanceledException or JsonException)
        {
            if (ct.IsCancellationRequested) throw;
            // The message never includes the request, so the key cannot end up in a log.
            _logger.LogWarning("Asking Groq failed to run: {Reason}", ex.GetType().Name);
            throw new DomainException("Groq's AI could not be reached. Try again in a moment.");
        }
    }

    internal static string Parse(string json)
    {
        using var document = JsonDocument.Parse(json);
        if (document.RootElement.TryGetProperty("choices", out var choices) && choices.ValueKind == JsonValueKind.Array && choices.GetArrayLength() > 0
            && choices[0].TryGetProperty("message", out var message) && message.TryGetProperty("content", out var content)
            && content.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(content.GetString()))
            return content.GetString()!;

        throw new DomainException("Groq's AI sent back no answer. Try again.");
    }

    private DomainException Failure(HttpStatusCode status, string body)
    {
        if (status == HttpStatusCode.TooManyRequests)
            return new DomainException("Groq's free allowance is used up for now. Try again in a minute, or tomorrow.");

        if (status is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
        {
            _logger.LogError("Groq refused the request ({Status}). Check Groq__ApiKey.", (int)status);
            return new DomainException("Groq refused the request. The key on the server may be wrong or switched off.");
        }

        var reason = GeminiTextModel.Reason(body);
        _logger.LogWarning("Groq request failed with {Status}: {Reason}", (int)status, reason);
        return new DomainException($"Groq's AI could not answer (it said {(int)status}{(reason.Length > 0 ? $": {reason}" : "")}). Try again.");
    }
}

/// <summary>
/// The model "Improve text with AI" uses: each free service whose key is set, best first, falling through to the next when one
/// cannot answer (its allowance is used up, or it is briefly down), so the writer still gets an answer. With none set up the
/// feature is switched off.
/// </summary>
public sealed class ChainedTextModel : ITextModel
{
    private readonly IReadOnlyList<ITextModel> _models;
    private readonly ILogger<ChainedTextModel> _logger;

    public ChainedTextModel(IEnumerable<ITextModel> models, ILogger<ChainedTextModel> logger)
    {
        _models = models.ToList();
        _logger = logger;
    }

    public bool IsEnabled => _models.Any(m => m.IsEnabled);

    public async Task<string> CompleteAsync(string instruction, string request, int maxTokens, double temperature, CancellationToken ct = default)
    {
        DomainException? last = null;
        foreach (var model in _models.Where(m => m.IsEnabled))
        {
            try
            {
                return await model.CompleteAsync(instruction, request, maxTokens, temperature, ct);
            }
            catch (DomainException ex)
            {
                last = ex;
                _logger.LogWarning("{Model} did not answer ({Reason}); trying the next one.", model.GetType().Name, ex.Message);
            }
        }

        throw last ?? new DomainException("The AI is not set up yet.");
    }
}
