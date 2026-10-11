namespace Articles.Infrastructure.Security;

public sealed class JwtOptions
{
    public const string SectionName = "Jwt";

    public string Issuer { get; set; } = "articles";
    public string Audience { get; set; } = "articles-web";

    /// <summary>
    /// Signing key. The development value lives in appsettings.Development.json; in any deployed
    /// environment this must come from an environment variable or secret store, never source control.
    /// </summary>
    public string Key { get; set; } = string.Empty;

    public int ExpiryMinutes { get; set; } = 60 * 24 * 7;
}
