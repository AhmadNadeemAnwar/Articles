using Articles.Application.Common;

namespace Articles.Infrastructure.Security;

/// <summary>
/// BCrypt with a per-password salt and a deliberately slow work factor, so a leaked
/// user table is not trivially crackable.
/// </summary>
public sealed class BCryptPasswordHasher : IPasswordHasher
{
    private const int WorkFactor = 12;

    public string Hash(string password) => BCrypt.Net.BCrypt.HashPassword(password, WorkFactor);

    /// <summary>Computed once, when the singleton is created, at the same work factor as real hashes.</summary>
    public string DummyHash { get; } = BCrypt.Net.BCrypt.HashPassword(Guid.NewGuid().ToString("N"), WorkFactor);

    public bool Verify(string password, string hash)
    {
        try
        {
            return BCrypt.Net.BCrypt.Verify(password, hash);
        }
        catch (BCrypt.Net.SaltParseException)
        {
            // A malformed stored hash must read as "wrong password", not blow up the login endpoint.
            return false;
        }
    }
}
