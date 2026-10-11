using Articles.Application.Admin.Dtos;
using Articles.Application.Common;
using Articles.Domain.Exceptions;
using Articles.Domain.Interfaces;
using Microsoft.Extensions.Logging;

namespace Articles.Application.Admin;

public interface IAdminAuthService
{
    Task<AdminSessionDto> LoginAsync(AdminLoginRequest request, CancellationToken ct = default);
}

/// <summary>
/// Admin sign-in needs an email on the admin list and a current authenticator code; there is no
/// password. Both are always checked and a failure reveals nothing about which one was wrong.
/// Because a 6-digit code is all that stands in the way, wrong guesses are counted by the stricter
/// <see cref="IAdminLoginAttemptTracker"/>.
/// </summary>
public sealed class AdminAuthService : IAdminAuthService
{
    private const string Failure = "Invalid email or code.";

    private readonly IUserRepository _users;
    private readonly ITokenService _tokens;
    private readonly IAdminDirectory _admins;
    private readonly ITotpVerifier _totp;
    private readonly IAdminLoginAttemptTracker _attempts;
    private readonly ILogger<AdminAuthService> _logger;
    private readonly IActivityLog _activity;

    public AdminAuthService(
        IUserRepository users,
        ITokenService tokens,
        IAdminDirectory admins,
        ITotpVerifier totp,
        IAdminLoginAttemptTracker attempts,
        ILogger<AdminAuthService> logger,
        IActivityLog? activity = null)
    {
        _activity = activity ?? NoActivityLog.Instance;
        _users = users;
        _tokens = tokens;
        _admins = admins;
        _totp = totp;
        _attempts = attempts;
        _logger = logger;
    }

    public async Task<AdminSessionDto> LoginAsync(AdminLoginRequest request, CancellationToken ct = default)
    {
        var email = request.Email.Trim().ToLowerInvariant();
        var lockKey = $"admin:{email}";

        if (_attempts.IsLockedOut(lockKey))
            throw new TooManyRequestsException("Too many failed attempts. Try again in an hour.");

        if (!_admins.IsConfigured)
        {
            _logger.LogError("Admin sign-in attempted but Admin:Emails and Admin:TotpSecret are not both configured");
            _attempts.RecordFailure(lockKey);
            throw new DomainException(Failure);
        }

        var user = await _users.GetByEmailAsync(email, ct);

        // Evaluate both unconditionally so the message never says which one failed.
        var isAdmin = _admins.IsAdmin(email);
        var step = _totp.Verify(request.Code);

        if (user is null || !isAdmin || step is null)
        {
            _attempts.RecordFailure(lockKey);
            _logger.LogWarning("Failed admin sign-in for {Email}", email);
            // Only attempts on a real admin address are worth showing; guesses at other addresses are
            // noise, and recording them would let a stranger fill the history. The lockout bounds these.
            if (isAdmin) await _activity.RecordAsync(email, Activity.FailedSignIn, ct: ct);
            throw new DomainException(Failure);
        }

        _totp.Consume(step.Value);
        _attempts.Clear(lockKey);

        var token = _tokens.CreateAdminSession(user!);
        _logger.LogInformation("Admin {Email} signed in", email);
        await _activity.RecordAsync(email, Activity.SignedIn, ct: ct);
        return new AdminSessionDto(token.Token, token.ExpiresAt, user!.Email, user.DisplayName);
    }
}
