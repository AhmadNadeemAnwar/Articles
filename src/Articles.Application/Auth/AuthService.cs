using Articles.Application.Auth.Dtos;
using Articles.Application.Common;
using Articles.Application.Posts.Mapping;
using Articles.Domain.Entities;
using Articles.Domain.Exceptions;
using Articles.Domain.Interfaces;

namespace Articles.Application.Auth;

public sealed class AuthService : IAuthService
{
    private readonly IUserRepository _users;
    private readonly IPasswordHasher _passwordHasher;
    private readonly ITokenService _tokens;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IPwnedPasswordChecker _pwned;
    private readonly ITurnstileVerifier _turnstile;
    private readonly ILoginAttemptTracker _attempts;

    public AuthService(
        IUserRepository users,
        IPasswordHasher passwordHasher,
        ITokenService tokens,
        IUnitOfWork unitOfWork,
        IPwnedPasswordChecker pwned,
        ITurnstileVerifier turnstile,
        ILoginAttemptTracker attempts)
    {
        _users = users;
        _passwordHasher = passwordHasher;
        _tokens = tokens;
        _unitOfWork = unitOfWork;
        _pwned = pwned;
        _turnstile = turnstile;
        _attempts = attempts;
    }

    public async Task<AuthResponse> RegisterAsync(RegisterRequest request, CancellationToken ct = default)
    {
        if (_turnstile.IsEnabled && !await _turnstile.VerifyAsync(request.TurnstileToken, ct))
            throw new DomainException("Complete the verification and try again.");

        var email = request.Email.Trim().ToLowerInvariant();
        var handle = SlugGenerator.Generate(request.Handle);

        if (string.IsNullOrEmpty(handle)) throw new DomainException("Handle must contain at least one letter or digit.");
        if (ReservedHandles.IsReserved(handle)) throw new DomainException($"The handle '{handle}' is reserved. Choose another.");
        if (await _users.EmailExistsAsync(email, ct)) throw new ConflictException("An account with that email already exists.");
        if (await _users.HandleExistsAsync(handle, ct)) throw new ConflictException($"The handle '{handle}' is taken.");

        if (await _pwned.IsPwnedAsync(request.Password, ct))
            throw new DomainException("That password has appeared in a known data breach. Choose a different one.");

        var user = new User(email, handle, request.DisplayName, _passwordHasher.Hash(request.Password));

        await _users.AddAsync(user, ct);
        await _unitOfWork.SaveChangesAsync(ct);

        return BuildResponse(user);
    }

    public async Task<AuthResponse> LoginAsync(LoginRequest request, CancellationToken ct = default)
    {
        var email = request.Email.Trim().ToLowerInvariant();

        // Keyed by the submitted email whether or not it exists, so locking out an account is
        // indistinguishable from locking out a made-up address.
        if (_attempts.IsLockedOut(email))
            throw new TooManyRequestsException("Too many failed sign-in attempts. Try again in a few minutes.");

        var user = await _users.GetByEmailAsync(email, ct);

        // Same message either way, so the endpoint cannot be used to enumerate registered emails.
        if (user is null || !_passwordHasher.Verify(request.Password, user.PasswordHash))
        {
            _attempts.RecordFailure(email);
            throw new DomainException("Invalid email or password.");
        }

        _attempts.Clear(email);
        return BuildResponse(user);
    }

    public async Task<CurrentUserDto> GetCurrentAsync(Guid userId, CancellationToken ct = default)
    {
        var user = await _users.GetByIdAsync(userId, ct) ?? throw new NotFoundException(nameof(User), userId);
        return user.ToCurrentUser();
    }

    private AuthResponse BuildResponse(User user)
    {
        var token = _tokens.Create(user);
        return new AuthResponse(token.Token, token.ExpiresAt, user.ToCurrentUser());
    }
}
