using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Articles.Application.Common;
using Articles.Domain.Entities;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace Articles.Infrastructure.Security;

public sealed class JwtTokenService : ITokenService
{
    private readonly JwtOptions _options;
    private readonly AdminOptions _admin;

    public JwtTokenService(IOptions<JwtOptions> options, IOptions<AdminOptions> admin)
    {
        _options = options.Value;
        _admin = admin.Value;
    }

    public AccessToken Create(User user) =>
        Build(user, TimeSpan.FromMinutes(_options.ExpiryMinutes), mfa: false);

    public AccessToken CreateAdminSession(User user) =>
        Build(user, TimeSpan.FromMinutes(Math.Clamp(_admin.SessionMinutes, 5, 720)), mfa: true);

    private AccessToken Build(User user, TimeSpan lifetime, bool mfa)
    {
        var expiresAt = DateTimeOffset.UtcNow.Add(lifetime);

        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, user.Id.ToString()),
            new(JwtRegisteredClaimNames.Email, user.Email),
            new(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString()),
            new("handle", user.Handle),
            new("displayName", user.DisplayName)
        };

        // Only the admin sign-in path adds this, after a one-time code has been verified.
        if (mfa) claims.Add(new Claim("mfa", "totp"));

        var credentials = new SigningCredentials(
            new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_options.Key)),
            SecurityAlgorithms.HmacSha256);

        var token = new JwtSecurityToken(
            issuer: _options.Issuer,
            audience: _options.Audience,
            claims: claims,
            expires: expiresAt.UtcDateTime,
            signingCredentials: credentials);

        return new AccessToken(new JwtSecurityTokenHandler().WriteToken(token), expiresAt);
    }
}
