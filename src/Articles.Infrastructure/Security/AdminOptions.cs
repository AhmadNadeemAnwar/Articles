using Articles.Application.Common;
using Microsoft.Extensions.Options;
using OtpNet;

namespace Articles.Infrastructure.Security;

public sealed class AdminOptions
{
    public const string SectionName = "Admin";

    /// <summary>Emails of the people allowed into the admin portal. Each must also be an existing account.</summary>
    public string[] Emails { get; set; } = [];

    /// <summary>Base32 secret shared with the authenticator app. Generate one with the AdminSetup tool; never commit it.</summary>
    public string TotpSecret { get; set; } = string.Empty;

    /// <summary>How long an admin session lasts. Deliberately much shorter than an ordinary sign-in.</summary>
    public int SessionMinutes { get; set; } = 120;
}

/// <summary>
/// Reads the admin list from live configuration on every call, so editing the environment variable
/// (and letting the host restart) is all it takes to add or remove an administrator.
/// </summary>
public sealed class ConfiguredAdminDirectory : IAdminDirectory
{
    private readonly IOptionsMonitor<AdminOptions> _options;

    public ConfiguredAdminDirectory(IOptionsMonitor<AdminOptions> options) => _options = options;

    public bool IsAdmin(string email) =>
        !string.IsNullOrWhiteSpace(email)
        && _options.CurrentValue.Emails.Any(e => string.Equals(e?.Trim(), email.Trim(), StringComparison.OrdinalIgnoreCase));

    public bool IsConfigured
    {
        get
        {
            var options = _options.CurrentValue;
            return options.Emails.Any(e => !string.IsNullOrWhiteSpace(e)) && TotpVerifier.TryDecodeSecret(options.TotpSecret, out _);
        }
    }
}

/// <summary>Verifies authenticator-app codes (RFC 6238: 6 digits, 30-second steps, SHA-1) and refuses replays.</summary>
public sealed class TotpVerifier : ITotpVerifier
{
    private readonly IOptionsMonitor<AdminOptions> _options;
    private readonly TimeProvider _time;
    private readonly object _gate = new();
    private long _lastConsumedStep;

    public TotpVerifier(IOptionsMonitor<AdminOptions> options, TimeProvider? time = null)
    {
        _options = options;
        _time = time ?? TimeProvider.System;
    }

    public static bool TryDecodeSecret(string? secret, out byte[] key)
    {
        key = [];
        if (string.IsNullOrWhiteSpace(secret)) return false;

        try
        {
            key = Base32Encoding.ToBytes(secret.Trim().Replace(" ", "").ToUpperInvariant());
            return key.Length >= 10;
        }
        catch (ArgumentException)
        {
            return false;
        }
    }

    public long? Verify(string code)
    {
        if (string.IsNullOrWhiteSpace(code) || !TryDecodeSecret(_options.CurrentValue.TotpSecret, out var key)) return null;

        var totp = new Totp(key, step: 30, mode: OtpHashMode.Sha1, totpSize: 6);

        // One step either side tolerates a phone clock that is a few seconds off.
        var valid = totp.VerifyTotp(_time.GetUtcNow().UtcDateTime, code.Trim(), out var matchedStep, new VerificationWindow(previous: 1, future: 1));
        if (!valid) return null;

        lock (_gate)
        {
            // A step at or before the last spent one is a replay of a code that already worked.
            return matchedStep > _lastConsumedStep ? matchedStep : null;
        }
    }

    public void Consume(long timeStep)
    {
        lock (_gate)
        {
            if (timeStep > _lastConsumedStep) _lastConsumedStep = timeStep;
        }
    }
}
