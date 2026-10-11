using System.Net;

namespace Articles.Api.Common;

public static class ClientIp
{
    /// <summary>Config key listing request headers, in priority order, that carry the real client address.</summary>
    public const string HeadersConfigKey = "RateLimiting:ClientIpHeaders";

    /// <summary>
    /// Returns the address of the actual visitor. On a managed host the TCP peer is the platform's
    /// proxy, not the visitor, so keying a limiter on it would put every visitor in one bucket (or,
    /// when proxies rotate, a different bucket per request, which is no limit at all).
    ///
    /// The named headers are only trustworthy when the platform's edge overwrites them, as
    /// Cloudflare does for CF-Connecting-IP. Leave the list empty for a server that is directly
    /// reachable, where a client could otherwise invent any value.
    /// </summary>
    public static string Resolve(HttpContext context, IReadOnlyList<string> trustedHeaders)
    {
        foreach (var header in trustedHeaders)
        {
            if (context.Request.Headers.TryGetValue(header, out var value)
                && IPAddress.TryParse(value.ToString().Trim(), out var address))
            {
                return address.ToString();
            }
        }

        return context.Connection.RemoteIpAddress?.ToString() ?? "unknown";
    }
}
