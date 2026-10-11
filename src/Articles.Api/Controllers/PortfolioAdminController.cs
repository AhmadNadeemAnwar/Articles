using System.Security.Claims;
using Articles.Api.Common;
using Articles.Application.Portfolio;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Articles.Api.Controllers;

/// <summary>Edit the portfolio site's blog, projects and updates. Saves become commits; the site rebuilds itself.</summary>
[ApiController]
[Route("api/v1/admin/portfolio")]
[Authorize(Policy = AdminRequirement.PolicyName)]
public class PortfolioAdminController : ControllerBase
{
    private readonly IPortfolioService _portfolio;

    public PortfolioAdminController(IPortfolioService portfolio) => _portfolio = portfolio;

    /// <summary>Whether GitHub access is configured, so the portal can show setup help instead of errors.</summary>
    [HttpGet("status")]
    public ActionResult<PortfolioStatusDto> Status() => Ok(_portfolio.GetStatus());

    [HttpGet("{collection}")]
    public async Task<ActionResult<IReadOnlyList<PortfolioEntrySummaryDto>>> List(string collection, CancellationToken ct) =>
        Ok(await _portfolio.ListAsync(collection, ct));

    [HttpGet("{collection}/{slug}")]
    public async Task<ActionResult<PortfolioEntryDto>> Get(string collection, string slug, CancellationToken ct) =>
        Ok(await _portfolio.GetAsync(collection, slug, ct));

    /// <summary>Creates an entry (no sha) or updates one (with the sha it was loaded at).</summary>
    [HttpPut("{collection}/{slug}")]
    public async Task<ActionResult<PortfolioEntryDto>> Save(string collection, string slug, SavePortfolioEntryRequest request, CancellationToken ct) =>
        Ok(await _portfolio.SaveAsync(collection, slug, request, Admin, ct));

    [HttpDelete("{collection}/{slug}")]
    public async Task<IActionResult> Delete(string collection, string slug, [FromQuery] string sha, CancellationToken ct)
    {
        await _portfolio.DeleteAsync(collection, slug, sha, Admin, ct);
        return NoContent();
    }

    private string Admin => User.FindFirstValue(ClaimTypes.Email) ?? User.FindFirstValue("email") ?? "unknown";
}
