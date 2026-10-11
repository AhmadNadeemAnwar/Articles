using Articles.Application.Common;

namespace Articles.Infrastructure.Security;

/// <summary>Used when the breached-password lookup is switched off, e.g. working offline.</summary>
public sealed class DisabledPwnedPasswordChecker : IPwnedPasswordChecker
{
    public Task<bool> IsPwnedAsync(string password, CancellationToken ct = default) => Task.FromResult(false);
}
