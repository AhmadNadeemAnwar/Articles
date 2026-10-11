using Articles.Application.Admin;
using Articles.Application.Admin.Dtos;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Articles.Api.Controllers;

[ApiController]
[Route("api/v1/admin/auth")]
public class AdminAuthController : ControllerBase
{
    private readonly IAdminAuthService _auth;

    public AdminAuthController(IAdminAuthService auth) => _auth = auth;

    /// <summary>An admin email plus a current authenticator code. The only way to obtain a token the admin routes accept.</summary>
    [HttpPost("login")]
    [AllowAnonymous]
    [ProducesResponseType(typeof(AdminSessionDto), StatusCodes.Status200OK)]
    public async Task<ActionResult<AdminSessionDto>> Login(AdminLoginRequest request, CancellationToken ct) =>
        Ok(await _auth.LoginAsync(request, ct));
}
