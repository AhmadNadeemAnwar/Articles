using System.Security.Cryptography;
using System.Text;
using Articles.Application.Common;
using Microsoft.Extensions.Logging;

namespace Articles.Infrastructure.Security;

/// <summary>
/// Queries the Have I Been Pwned range API using k-anonymity: only the first five characters of the
/// password's SHA-1 leave this server, never the password or its full hash. The service is free
/// and needs no key.
///
/// Fails open on purpose. A third-party outage must not stop people from signing up, and the
/// local common-password screen has already run by the time this is called.
/// </summary>
public sealed class PwnedPasswordChecker : IPwnedPasswordChecker
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(2);

    private readonly HttpClient _http;
    private readonly ILogger<PwnedPasswordChecker> _logger;

    public PwnedPasswordChecker(HttpClient http, ILogger<PwnedPasswordChecker> logger)
    {
        _http = http;
        _logger = logger;
    }

    public async Task<bool> IsPwnedAsync(string password, CancellationToken ct = default)
    {
        try
        {
            var hash = Convert.ToHexString(SHA1.HashData(Encoding.UTF8.GetBytes(password)));
            var prefix = hash[..5];
            var suffix = hash[5..];

            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(Timeout);

            using var request = new HttpRequestMessage(HttpMethod.Get, $"https://api.pwnedpasswords.com/range/{prefix}");
            // Padding makes every response a similar size, so response length does not hint at the prefix's matches.
            request.Headers.Add("Add-Padding", "true");

            using var response = await _http.SendAsync(request, timeout.Token);
            if (!response.IsSuccessStatusCode) return false;

            var body = await response.Content.ReadAsStringAsync(timeout.Token);

            foreach (var line in body.Split('\n', StringSplitOptions.RemoveEmptyEntries))
            {
                var parts = line.Trim().Split(':');
                if (parts.Length != 2) continue;
                if (!parts[0].Equals(suffix, StringComparison.OrdinalIgnoreCase)) continue;

                // Padding rows carry a count of 0 and are not real matches.
                return int.TryParse(parts[1], out var count) && count > 0;
            }

            return false;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or OperationCanceledException)
        {
            _logger.LogWarning(ex, "Breached-password lookup failed; allowing the password");
            return false;
        }
    }
}
