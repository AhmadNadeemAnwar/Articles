using Articles.Domain.Entities;

namespace Articles.Application.Common;

/// <summary>Password hashing, kept behind an interface so the algorithm is an infrastructure concern.</summary>
public interface IPasswordHasher
{
    string Hash(string password);
    bool Verify(string password, string hash);

    /// <summary>A valid hash of a throwaway secret, for checking a password when no real account exists so timing stays uniform.</summary>
    string DummyHash { get; }
}

public sealed record AccessToken(string Token, DateTimeOffset ExpiresAt);

public interface ITokenService
{
    AccessToken Create(User user);

    /// <summary>
    /// A short-lived token for the admin portal. It carries a claim proving a one-time code was
    /// verified, which ordinary sign-in tokens never have, so a stolen password alone cannot reach admin routes.
    /// </summary>
    AccessToken CreateAdminSession(User user);
}

/// <summary>The authenticated caller for the current request, resolved from the bearer token.</summary>
public interface ICurrentUser
{
    Guid? UserId { get; }
    bool IsAuthenticated { get; }

    /// <summary>Returns the caller's id, or throws if the request is anonymous.</summary>
    Guid RequireUserId();
}

/// <summary>Checks a password against known breach corpora. Implementations must fail open (return false) on any error.</summary>
public interface IPwnedPasswordChecker
{
    Task<bool> IsPwnedAsync(string password, CancellationToken ct = default);
}

/// <summary>Bot check for sign-up. When no secret is configured, <see cref="IsEnabled"/> is false and verification is skipped.</summary>
public interface ITurnstileVerifier
{
    bool IsEnabled { get; }
    Task<bool> VerifyAsync(string? token, CancellationToken ct = default);
}

/// <summary>Counts failed sign-ins per account, so a distributed attack cannot get around per-IP limits.</summary>
public interface ILoginAttemptTracker
{
    bool IsLockedOut(string key);
    void RecordFailure(string key);
    void Clear(string key);
}

/// <summary>
/// The counter for admin sign-in. Kept separate because that sign-in rests on a 6-digit code alone,
/// so it allows far fewer wrong guesses than a password does.
/// </summary>
public interface IAdminLoginAttemptTracker : ILoginAttemptTracker;

/// <summary>Decides who counts as an administrator. Re-evaluated on every request, so removing someone takes effect at once.</summary>
public interface IAdminDirectory
{
    bool IsAdmin(string email);

    /// <summary>False until both an admin email and a one-time-code secret are configured; admin sign-in refuses everyone until then.</summary>
    bool IsConfigured { get; }
}

/// <summary>Verifies authenticator-app (TOTP) codes.</summary>
public interface ITotpVerifier
{
    /// <summary>Returns the matched 30-second time step if the code is currently valid and has not been used, otherwise null.</summary>
    long? Verify(string code);

    /// <summary>Records a step as spent so the same code cannot be replayed.</summary>
    void Consume(long timeStep);
}
