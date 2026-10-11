using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Articles.Application.Common;
using Articles.Domain.Exceptions;

namespace Articles.Api.Common;

/// <summary>Reads the caller's identity from the validated bearer token on the current request.</summary>
public sealed class CurrentUser : ICurrentUser
{
    private readonly IHttpContextAccessor _accessor;

    public CurrentUser(IHttpContextAccessor accessor) => _accessor = accessor;

    public Guid? UserId
    {
        get
        {
            var principal = _accessor.HttpContext?.User;
            if (principal?.Identity?.IsAuthenticated != true) return null;

            // JwtSecurityTokenHandler maps "sub" to NameIdentifier by default; accept either.
            var raw = principal.FindFirstValue(ClaimTypes.NameIdentifier)
                      ?? principal.FindFirstValue(JwtRegisteredClaimNames.Sub);

            return Guid.TryParse(raw, out var id) ? id : null;
        }
    }

    public bool IsAuthenticated => UserId.HasValue;

    public Guid RequireUserId() =>
        UserId ?? throw new ForbiddenException("You must be signed in to do that.");
}
