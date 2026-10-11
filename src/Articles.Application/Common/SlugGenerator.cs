using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace Articles.Application.Common;

/// <summary>Turns human titles into URL-safe slugs. Deterministic, so the same title always yields the same base.</summary>
public static class SlugGenerator
{
    private const int MaxLength = 80;

    public static string Generate(string input)
    {
        if (string.IsNullOrWhiteSpace(input)) return string.Empty;

        // Strip accents so "Café" and "Cafe" collapse to the same slug.
        var normalized = input.Normalize(NormalizationForm.FormD);
        var builder = new StringBuilder(normalized.Length);
        var lastWasDash = false;

        foreach (var ch in normalized)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(ch) == UnicodeCategory.NonSpacingMark) continue;

            if (char.IsLetterOrDigit(ch))
            {
                builder.Append(char.ToLowerInvariant(ch));
                lastWasDash = false;
            }
            else if (!lastWasDash && builder.Length > 0)
            {
                builder.Append('-');
                lastWasDash = true;
            }
        }

        var slug = builder.ToString().Trim('-');
        if (slug.Length > MaxLength)
        {
            slug = slug[..MaxLength].TrimEnd('-');
            var lastDash = slug.LastIndexOf('-');
            if (lastDash > MaxLength / 2) slug = slug[..lastDash];
        }

        return slug;
    }

    public const int CodeLength = 5;

    /// <summary>Letters and digits that cannot be mistaken for one another when read aloud or copied by hand (no 0/o, 1/l/i).</summary>
    private const string CodeAlphabet = "23456789abcdefghjkmnpqrstuvwxyz";

    /// <summary>
    /// Appends a short random code, as in "today-for-tomorrow-k3x9p". Every post address ends in
    /// one, so two posts with the same title never collide and an address cannot be guessed from a title.
    /// </summary>
    public static string WithCode(string slug)
    {
        var code = RandomNumberGenerator.GetString(CodeAlphabet, CodeLength);
        return string.IsNullOrEmpty(slug) ? code : $"{slug}-{code}";
    }

    /// <summary>
    /// Appends a longer random discriminator. The fallback if short codes keep colliding, which keeps
    /// slug allocation a single insert attempt instead of a count-and-retry loop.
    /// </summary>
    public static string WithSuffix(string slug)
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        return string.IsNullOrEmpty(slug) ? suffix : $"{slug}-{suffix}";
    }
}
