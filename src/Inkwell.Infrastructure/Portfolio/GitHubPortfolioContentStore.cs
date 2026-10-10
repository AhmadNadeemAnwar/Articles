using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Inkwell.Application.Portfolio;
using Inkwell.Domain.Exceptions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Inkwell.Infrastructure.Portfolio;

public sealed class PortfolioOptions
{
    public const string SectionName = "Portfolio";

    /// <summary>The portfolio's repository as "owner/name".</summary>
    public string Repo { get; set; } = string.Empty;
    public string Branch { get; set; } = "main";
    public string ContentRoot { get; set; } = "src/content";

    /// <summary>Only needed for GitHub Enterprise or testing. Must be https, or http on localhost.</summary>
    public string ApiBaseUrl { get; set; } = "https://api.github.com";

    /// <summary>
    /// A fine-grained GitHub token limited to that one repository with Contents: read and write.
    /// A secret: set it as an environment variable on the host, never in a file or in chat.
    /// </summary>
    public string Token { get; set; } = string.Empty;
}

/// <summary>Stores portfolio content as commits to a GitHub repository, so the site rebuilds itself and every change is in git history.</summary>
public sealed partial class GitHubPortfolioContentStore : IPortfolioContentStore
{
    // GitHub owners are letters, digits and hyphens only; repository names may also use dots and
    // underscores but can never be "." or ".." (which would let the URL walk up out of /repos/).
    [GeneratedRegex(@"^[A-Za-z0-9][A-Za-z0-9-]{0,38}/(?!\.{1,2}$)[A-Za-z0-9_.-]{1,100}$")]
    private static partial Regex RepoPattern();

    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(20);

    private readonly HttpClient _http;
    private readonly IOptionsMonitor<PortfolioOptions> _options;
    private readonly ILogger<GitHubPortfolioContentStore> _logger;

    public GitHubPortfolioContentStore(HttpClient http, IOptionsMonitor<PortfolioOptions> options, ILogger<GitHubPortfolioContentStore> logger)
    {
        _http = http;
        _options = options;
        _logger = logger;
    }

    private PortfolioOptions Options => _options.CurrentValue;

    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(Options.Token)
        && RepoPattern().IsMatch(Options.Repo ?? string.Empty)
        && BaseUrl is not null;

    /// <summary>The token is only ever sent over https, or to this same machine; anything else is treated as misconfiguration.</summary>
    private string? BaseUrl
    {
        get
        {
            var configured = string.IsNullOrWhiteSpace(Options.ApiBaseUrl) ? "https://api.github.com" : Options.ApiBaseUrl.Trim();
            if (!Uri.TryCreate(configured, UriKind.Absolute, out var uri)) return null;

            var local = uri.IsLoopback;
            if (uri.Scheme != Uri.UriSchemeHttps && !(uri.Scheme == Uri.UriSchemeHttp && local)) return null;
            return configured.TrimEnd('/');
        }
    }
    public string? Repo => IsConfigured ? Options.Repo : null;
    public string? Branch => IsConfigured ? Options.Branch : null;
    public string ContentRoot => string.IsNullOrWhiteSpace(Options.ContentRoot) ? "src/content" : Options.ContentRoot;

    public async Task<IReadOnlyList<RepoFile>> ListAsync(string directory, CancellationToken ct = default)
    {
        using var response = await SendAsync(HttpMethod.Get, ContentsUrl(directory) + RefQuery(), null, ct);
        if (response.StatusCode == HttpStatusCode.NotFound) return [];
        await EnsureSuccessAsync(response, ct);

        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
        if (json.RootElement.ValueKind != JsonValueKind.Array) return [];

        return json.RootElement.EnumerateArray()
            .Where(e => e.GetProperty("type").GetString() == "file")
            .Select(e => new RepoFile(e.GetProperty("name").GetString()!, e.GetProperty("path").GetString()!, e.GetProperty("sha").GetString()!, null))
            .ToList();
    }

    public async Task<RepoFile?> GetAsync(string path, CancellationToken ct = default)
    {
        using var response = await SendAsync(HttpMethod.Get, ContentsUrl(path) + RefQuery(), null, ct);
        if (response.StatusCode == HttpStatusCode.NotFound) return null;
        await EnsureSuccessAsync(response, ct);

        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
        var root = json.RootElement;
        if (root.ValueKind != JsonValueKind.Object || root.GetProperty("type").GetString() != "file") return null;

        var encoded = root.GetProperty("content").GetString() ?? string.Empty;
        var content = Encoding.UTF8.GetString(Convert.FromBase64String(encoded.Replace("\n", "").Replace("\r", "")));

        return new RepoFile(root.GetProperty("name").GetString()!, root.GetProperty("path").GetString()!, root.GetProperty("sha").GetString()!, content);
    }

    public async Task<string> PutAsync(string path, string content, string? sha, string message, CancellationToken ct = default)
    {
        var body = new Dictionary<string, object?>
        {
            ["message"] = message,
            ["content"] = Convert.ToBase64String(Encoding.UTF8.GetBytes(content)),
            ["branch"] = Options.Branch
        };
        if (sha is not null) body["sha"] = sha;

        using var response = await SendAsync(HttpMethod.Put, ContentsUrl(path), body, ct);
        await EnsureSuccessAsync(response, ct);

        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
        return json.RootElement.GetProperty("content").GetProperty("sha").GetString()!;
    }

    public async Task DeleteAsync(string path, string sha, string message, CancellationToken ct = default)
    {
        var body = new Dictionary<string, object?> { ["message"] = message, ["sha"] = sha, ["branch"] = Options.Branch };

        using var response = await SendAsync(HttpMethod.Delete, ContentsUrl(path), body, ct);
        await EnsureSuccessAsync(response, ct);
    }

    // ---- plumbing ------------------------------------------------------------------------

    private string ContentsUrl(string path)
    {
        // Each segment is encoded separately, so a crafted path cannot climb out with ".." or smuggle in a query.
        var segments = path.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (segments.Any(s => s is "." or ".."))
            throw new DomainException("That file path is not allowed.");

        return $"{BaseUrl}/repos/{Options.Repo}/contents/{string.Join('/', segments.Select(Uri.EscapeDataString))}";
    }

    private string RefQuery() => $"?ref={Uri.EscapeDataString(Options.Branch)}";

    private async Task<HttpResponseMessage> SendAsync(HttpMethod method, string url, object? body, CancellationToken ct)
    {
        if (!IsConfigured)
            throw new ServiceUnavailableException("Portfolio editing is not set up yet. Add the GitHub settings described in ADMIN.md.");

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(Timeout);

        var request = new HttpRequestMessage(method, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", Options.Token);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        request.Headers.UserAgent.Add(new ProductInfoHeaderValue("articles-admin", "1.0"));
        request.Headers.Add("X-GitHub-Api-Version", "2022-11-28");
        if (body is not null) request.Content = JsonContent.Create(body);

        try
        {
            return await _http.SendAsync(request, timeout.Token);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or OperationCanceledException)
        {
            _logger.LogWarning(ex, "GitHub request failed: {Method} {Url}", method, url);
            throw new ExternalServiceException("Could not reach GitHub. Try again in a moment.");
        }
    }

    private async Task EnsureSuccessAsync(HttpResponseMessage response, CancellationToken ct)
    {
        if (response.IsSuccessStatusCode) return;

        var detail = await SafeMessageAsync(response, ct);
        _logger.LogWarning("GitHub returned {Status}: {Detail}", (int)response.StatusCode, detail);

        throw response.StatusCode switch
        {
            // 409 and 422 are how GitHub reports a stale sha or a file that already exists.
            HttpStatusCode.Conflict or HttpStatusCode.UnprocessableEntity =>
                new ConflictException("GitHub reports this file changed or already exists. Reload it and try again."),
            HttpStatusCode.NotFound => new NotFoundException("The file was not found in the repository."),
            HttpStatusCode.Unauthorized => new ExternalServiceException("GitHub rejected the token. Create a new one and update the setting on the host."),
            HttpStatusCode.Forbidden when detail.Contains("rate limit", StringComparison.OrdinalIgnoreCase) =>
                new ExternalServiceException("GitHub's rate limit was reached. Try again in a few minutes."),
            HttpStatusCode.Forbidden => new ExternalServiceException("GitHub refused the request. Check the token has Contents read and write on the portfolio repository."),
            _ => new ExternalServiceException($"GitHub returned an error ({(int)response.StatusCode}).")
        };
    }

    private static async Task<string> SafeMessageAsync(HttpResponseMessage response, CancellationToken ct)
    {
        try
        {
            using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
            return json.RootElement.TryGetProperty("message", out var m) ? m.GetString() ?? string.Empty : string.Empty;
        }
        catch (JsonException)
        {
            return string.Empty;
        }
    }
}
