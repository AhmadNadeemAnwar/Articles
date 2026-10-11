using System.Text.RegularExpressions;

namespace Articles.Domain.Common;

/// <summary>
/// Allow-list checks for user-supplied URLs. Anything outside http(s) is rejected, which rules out
/// javascript:, data:, file: and similar schemes regardless of how a client chooses to render them.
/// </summary>
public static class UrlRules
{
    public static bool IsHttpOrHttps(string? value) =>
        TryParse(value, out var uri) && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps);

    /// <summary>Images are https-only: the site is served over https, so http images would be blocked as mixed content.</summary>
    public static bool IsHttps(string? value) =>
        TryParse(value, out var uri) && uri.Scheme == Uri.UriSchemeHttps;

    private static readonly Regex StoredImage = new(
        @"^/api/v1/images/[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    /// <summary>The address of a picture uploaded to this site. Relative, so it works wherever the API is hosted.</summary>
    public static string StoredImagePath(Guid id) => $"/api/v1/images/{id:D}";

    /// <summary>Exactly the shape <see cref="StoredImagePath"/> produces, and nothing else on this site.</summary>
    public static bool IsStoredImagePath(string? value) => value is not null && StoredImage.IsMatch(value.Trim());

    /// <summary>Where a picture may come from: uploaded here, or an https link elsewhere.</summary>
    public static bool IsImageReference(string? value) => IsStoredImagePath(value) || IsHttps(value);

    private static bool TryParse(string? value, out Uri uri)
    {
        uri = null!;
        return !string.IsNullOrWhiteSpace(value)
            && Uri.TryCreate(value.Trim(), UriKind.Absolute, out uri!)
            && !string.IsNullOrEmpty(uri.Host);
    }
}
