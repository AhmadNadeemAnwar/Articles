using System.Text.Json;
using Articles.Application.Grammar;
using Articles.Domain.Exceptions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Articles.Infrastructure.Grammar;

public sealed class GrammarOptions
{
    public const string SectionName = "Grammar";

    /// <summary>The free public LanguageTool service: no key, about 20 checks a minute, 20 KB each.</summary>
    public string BaseUrl { get; set; } = "https://api.languagetool.org";

    public string Language { get; set; } = "en-US";
}

/// <summary>Checks text with LanguageTool's free public API.</summary>
public sealed class LanguageToolChecker : IGrammarChecker
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(20);
    private const int MaxSuggestions = 5;

    private readonly HttpClient _http;
    private readonly GrammarOptions _options;
    private readonly ILogger<LanguageToolChecker> _logger;

    public LanguageToolChecker(HttpClient http, IOptions<GrammarOptions> options, ILogger<LanguageToolChecker> logger)
    {
        _http = http;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<GrammarResult> CheckAsync(string text, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(text)) return new GrammarResult([]);

        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(Timeout);

            using var content = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["text"] = text,
                ["language"] = _options.Language
            });

            using var response = await _http.PostAsync($"{_options.BaseUrl.TrimEnd('/')}/v2/check", content, timeout.Token);

            if (response.StatusCode == System.Net.HttpStatusCode.TooManyRequests)
                throw new DomainException("The free grammar service is busy. Wait a minute and try again.");
            if (!response.IsSuccessStatusCode)
                throw new DomainException("The grammar service could not check this text. Try again in a moment.");

            return Parse(await response.Content.ReadAsStringAsync(timeout.Token));
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or OperationCanceledException or JsonException)
        {
            if (ct.IsCancellationRequested) throw;
            _logger.LogWarning(ex, "Grammar check failed to run");
            throw new DomainException("The grammar service could not be reached. Try again in a moment.");
        }
    }

    internal static GrammarResult Parse(string json)
    {
        using var document = JsonDocument.Parse(json);
        var matches = new List<GrammarMatch>();

        if (document.RootElement.TryGetProperty("matches", out var list))
        {
            foreach (var item in list.EnumerateArray())
            {
                var replacements = item.TryGetProperty("replacements", out var r)
                    ? r.EnumerateArray().Select(x => x.TryGetProperty("value", out var v) ? v.GetString() : null)
                        .Where(v => !string.IsNullOrEmpty(v)).Take(MaxSuggestions).Select(v => v!).ToList()
                    : [];

                var category = item.TryGetProperty("rule", out var rule) && rule.TryGetProperty("category", out var c) && c.TryGetProperty("name", out var n)
                    ? n.GetString() ?? "" : "";

                matches.Add(new GrammarMatch(
                    item.GetProperty("offset").GetInt32(),
                    item.GetProperty("length").GetInt32(),
                    item.TryGetProperty("message", out var m) ? m.GetString() ?? "" : "",
                    category,
                    replacements));
            }
        }

        return new GrammarResult(matches);
    }
}
