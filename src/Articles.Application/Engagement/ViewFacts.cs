using System.Text.RegularExpressions;

namespace Articles.Application.Engagement;

/// <summary>
/// The coarse facts kept about a view, worked out here so nothing finer is ever stored: a country code (never an address),
/// a kind of device (the browser string itself is thrown away), and the bare name of the site the reader came from.
/// </summary>
public static partial class ViewFacts
{
    /// <summary>A two-letter country code, or empty. Cloudflare's "XX" (unknown) and "T1" (Tor) count as unknown.</summary>
    public static string Country(string? value)
    {
        var code = (value ?? string.Empty).Trim().ToUpperInvariant();
        return TwoLetters().IsMatch(code) && code is not "XX" ? code : string.Empty;
    }

    public static string Device(string? userAgent)
    {
        var agent = userAgent ?? string.Empty;
        if (Tablet().IsMatch(agent)) return "tablet";
        if (Phone().IsMatch(agent)) return "phone";
        return "computer";
    }

    /// <summary>
    /// The bare name of the site a reader came from ("google.com" from "https://www.google.com/search?q=…"), or empty for a
    /// reader who came straight to the post or moved within this site. Never the path or the query, which can be personal.
    /// </summary>
    public static string Source(string? referrerHost, IEnumerable<string> ownSites)
    {
        var host = (referrerHost ?? string.Empty).Trim().ToLowerInvariant();
        if (host.StartsWith("www.", StringComparison.Ordinal)) host = host[4..];
        if (host.Length == 0 || host.Length > Entities.MaxSource || !HostName().IsMatch(host)) return string.Empty;

        foreach (var own in ownSites)
        {
            var site = own.Trim().ToLowerInvariant();
            if (host == site || host.EndsWith("." + site, StringComparison.Ordinal)) return string.Empty;
        }
        return host;
    }

    private static class Entities
    {
        public const int MaxSource = Articles.Domain.Entities.PostViewStat.MaxSourceLength;
    }

    [GeneratedRegex("^[A-Z]{2}$")]
    private static partial Regex TwoLetters();

    [GeneratedRegex("iPad|Tablet|Nexus (7|9|10)|SM-T|Kindle|Silk|Android(?!.*Mobi)", RegexOptions.IgnoreCase)]
    private static partial Regex Tablet();

    [GeneratedRegex("Mobi|iPhone|iPod|Android.*Mobile|Windows Phone", RegexOptions.IgnoreCase)]
    private static partial Regex Phone();

    [GeneratedRegex(@"^[a-z0-9]([a-z0-9-]*[a-z0-9])?(\.[a-z0-9]([a-z0-9-]*[a-z0-9])?)+$")]
    private static partial Regex HostName();
}
