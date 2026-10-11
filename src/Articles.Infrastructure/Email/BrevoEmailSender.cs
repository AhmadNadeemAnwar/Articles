using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using Articles.Application.Subscriptions;
using Articles.Domain.Common;
using Articles.Infrastructure.Security;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Articles.Infrastructure.Email;

public sealed class EmailOptions
{
    public const string SectionName = "Email";

    /// <summary>The Brevo API key. Set it on the host (Email__ApiKey), never in the repository. Empty switches email, and subscribing, off.</summary>
    public string ApiKey { get; set; } = string.Empty;

    /// <summary>The address emails come from. Must belong to a domain verified in Brevo.</summary>
    public string FromAddress { get; set; } = string.Empty;

    public string FromName { get; set; } = "Articles by Ahmad Nadeem";

    /// <summary>The public site's address, for links in emails. No trailing slash.</summary>
    public string SiteUrl { get; set; } = "http://localhost:5173";

    /// <summary>Brevo's free plan allows 300 emails a day. Announcements stop at this many per run.</summary>
    public int DailyLimit { get; set; } = 300;

    public string ApiBaseUrl { get; set; } = "https://api.brevo.com";
}

/// <summary>Sends email through Brevo's transactional API.</summary>
public sealed class BrevoEmailSender : IEmailSender
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(15);

    private readonly HttpClient _http;
    private readonly EmailOptions _options;
    private readonly ILogger<BrevoEmailSender> _logger;

    public BrevoEmailSender(HttpClient http, IOptions<EmailOptions> options, ILogger<BrevoEmailSender> logger)
    {
        _http = http;
        _options = options.Value;
        _logger = logger;
    }

    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(_options.ApiKey)
        && !string.IsNullOrWhiteSpace(_options.FromAddress)
        && UrlRules.IsHttpOrHttps(_options.SiteUrl);

    public int DailyLimit => Math.Clamp(_options.DailyLimit, 1, 10_000);

    public string SiteUrl => _options.SiteUrl.TrimEnd('/');

    public async Task<bool> SendAsync(EmailMessage message, CancellationToken ct = default)
    {
        if (!IsConfigured) return false;

        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(Timeout);

            using var request = new HttpRequestMessage(HttpMethod.Post, $"{_options.ApiBaseUrl.TrimEnd('/')}/v3/smtp/email");
            request.Headers.Add("api-key", _options.ApiKey);
            request.Headers.Add("accept", "application/json");

            var headers = new Dictionary<string, string>();
            if (message.UnsubscribeUrl is not null)
            {
                // Lets mail apps show their own "Unsubscribe" button, which keeps people from pressing "Spam" instead.
                headers["List-Unsubscribe"] = $"<{message.UnsubscribeUrl}>";
            }

            request.Content = JsonContent.Create(new
            {
                sender = new { name = _options.FromName, email = _options.FromAddress },
                to = new[] { new { email = message.To } },
                subject = message.Subject,
                textContent = message.Text,
                htmlContent = message.Html,
                headers = headers.Count > 0 ? headers : null
            }, options: new System.Text.Json.JsonSerializerOptions
            {
                DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
            });

            using var response = await _http.SendAsync(request, timeout.Token);
            if (response.IsSuccessStatusCode) return true;

            // The body can explain a refusal (unverified sender, daily allowance used up). It never contains the key.
            var body = await response.Content.ReadAsStringAsync(timeout.Token);
            _logger.LogWarning("Brevo refused an email with {Status}: {Body}", (int)response.StatusCode, body.Length > 300 ? body[..300] : body);
            return false;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or OperationCanceledException)
        {
            if (ct.IsCancellationRequested) throw;
            _logger.LogWarning(ex, "Brevo could not be reached");
            return false;
        }
    }
}

/// <summary>
/// Signs the links in emails with a key derived from the site's signing key, so a separate secret
/// does not have to be set up. A token names one subscriber and one purpose; a confirm link cannot
/// be used to unsubscribe someone, and neither can be made up without the key.
/// </summary>
public sealed class SubscriberTokens : ISubscriberTokens
{
    private const int SignatureBytes = 16;

    private readonly byte[] _key;

    public SubscriberTokens(IOptions<JwtOptions> jwt)
    {
        // A different key from the one that signs sessions, derived from it: knowing one of these
        // tokens tells nothing about sign-in tokens.
        _key = HMACSHA256.HashData(Encoding.UTF8.GetBytes(jwt.Value.Key), "articles.subscriber-links.v1"u8.ToArray());
    }

    public string Create(Guid subscriberId, string purpose) =>
        $"{Encode(subscriberId.ToByteArray())}.{Encode(Sign(subscriberId, purpose))}";

    public Guid? Read(string? token, string purpose)
    {
        if (string.IsNullOrWhiteSpace(token) || token.Length > 100) return null;

        var parts = token.Trim().Split('.');
        if (parts.Length != 2) return null;

        var idBytes = Decode(parts[0]);
        var signature = Decode(parts[1]);
        if (idBytes is not { Length: 16 } || signature is not { Length: SignatureBytes }) return null;

        var id = new Guid(idBytes);
        return CryptographicOperations.FixedTimeEquals(signature, Sign(id, purpose)) ? id : null;
    }

    private byte[] Sign(Guid id, string purpose) =>
        HMACSHA256.HashData(_key, Encoding.UTF8.GetBytes($"{purpose}:{id:N}"))[..SignatureBytes];

    private static string Encode(byte[] bytes) => Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private static byte[]? Decode(string value)
    {
        try
        {
            var padded = value.Replace('-', '+').Replace('_', '/');
            padded = padded.PadRight(padded.Length + (4 - padded.Length % 4) % 4, '=');
            return Convert.FromBase64String(padded);
        }
        catch (FormatException)
        {
            return null;
        }
    }
}
