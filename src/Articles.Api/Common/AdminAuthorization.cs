using System.Security.Claims;
using Articles.Application.Common;
using Microsoft.AspNetCore.Authorization;

namespace Articles.Api.Common;

public sealed class AdminRequirement : IAuthorizationRequirement
{
    public const string PolicyName = "Admin";
}

/// <summary>
/// Grants access to admin routes only when the token proves a one-time code was verified AND the
/// email on it is on the admin list right now. The list is checked on every request, not just at
/// sign-in, so removing an email locks that person out immediately instead of when their token expires.
/// </summary>
public sealed class AdminAuthorizationHandler : AuthorizationHandler<AdminRequirement>
{
    private readonly IAdminDirectory _admins;

    public AdminAuthorizationHandler(IAdminDirectory admins) => _admins = admins;

    protected override Task HandleRequirementAsync(AuthorizationHandlerContext context, AdminRequirement requirement)
    {
        var user = context.User;
        var email = user.FindFirstValue(ClaimTypes.Email) ?? user.FindFirstValue("email");

        if (user.HasClaim("mfa", "totp") && email is not null && _admins.IsAdmin(email))
            context.Succeed(requirement);

        return Task.CompletedTask;
    }
}
