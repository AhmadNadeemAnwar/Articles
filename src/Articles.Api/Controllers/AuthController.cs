using Articles.Application.Auth;
using Articles.Application.Auth.Dtos;
using Articles.Application.Common;
using Articles.Api.Common;
using Articles.Domain.Exceptions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace Articles.Api.Controllers;

[ApiController]
[Route("api/v1/auth")]
public class AuthController : ControllerBase
{
    private readonly IAuthService _auth;
    private readonly ICurrentUser _currentUser;
    private readonly AccountOptions _accounts;

    public AuthController(IAuthService auth, ICurrentUser currentUser, IOptions<AccountOptions> accounts)
    {
        _auth = auth;
        _currentUser = currentUser;
        _accounts = accounts.Value;
    }

    /// <summary>What the client needs to know before it decides which sign-in and sign-up prompts to show.</summary>
    [HttpGet("options")]
    [AllowAnonymous]
    [ProducesResponseType(typeof(AuthOptionsDto), StatusCodes.Status200OK)]
    public ActionResult<AuthOptionsDto> Options() =>
        Ok(new AuthOptionsDto(_accounts.AllowPublicSignUp && _accounts.AllowPasswordSignIn, _accounts.AllowPasswordSignIn));

    /// <summary>Creates an account and returns a signed token.</summary>
    [HttpPost("register")]
    [ProducesResponseType(typeof(AuthResponse), StatusCodes.Status201Created)]
    public async Task<ActionResult<AuthResponse>> Register(RegisterRequest request, CancellationToken ct)
    {
        // Enforced here, not just hidden in the UI: a closed site must refuse direct API calls too.
        if (!_accounts.AllowPublicSignUp || !_accounts.AllowPasswordSignIn) throw new ForbiddenException("Sign-ups are closed.");

        var response = await _auth.RegisterAsync(request, ct);
        return Created($"/api/v1/users/{response.User.Handle}", response);
    }

    [HttpPost("login")]
    [ProducesResponseType(typeof(AuthResponse), StatusCodes.Status200OK)]
    public async Task<ActionResult<AuthResponse>> Login(LoginRequest request, CancellationToken ct)
    {
        // Refused before any password is looked at, so a closed door cannot be used to test passwords.
        if (!_accounts.AllowPasswordSignIn) throw new ForbiddenException("Sign-in is closed.");

        return Ok(await _auth.LoginAsync(request, ct));
    }

    /// <summary>Returns the signed-in user, used by the client to restore a session on page load.</summary>
    [HttpGet("me")]
    [Authorize]
    [ProducesResponseType(typeof(CurrentUserDto), StatusCodes.Status200OK)]
    public async Task<ActionResult<CurrentUserDto>> Me(CancellationToken ct) =>
        Ok(await _auth.GetCurrentAsync(_currentUser.RequireUserId(), ct));
}

public sealed record AuthOptionsDto(bool AllowPublicSignUp, bool AllowPasswordSignIn);
