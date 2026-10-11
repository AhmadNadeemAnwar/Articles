using System.Security.Claims;
using Articles.Api.Common;
using Articles.Application.Site;
using Articles.Application.Subscriptions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Articles.Api.Controllers;

/// <summary>How the public site looks and how its posts are shelved. Read by every visitor.</summary>
[ApiController]
[Route("api/v1/site")]
public class SiteController : ControllerBase
{
    private readonly ISiteService _site;
    private readonly ISubscriptionService _subscriptions;

    public SiteController(ISiteService site, ISubscriptionService subscriptions)
    {
        _site = site;
        _subscriptions = subscriptions;
    }

    [HttpGet]
    [AllowAnonymous]
    public async Task<ActionResult<SiteDto>> Get(CancellationToken ct) =>
        Ok(await _site.GetPublicAsync(ct) with { SubscribeEnabled = _subscriptions.IsAvailable });
}

/// <summary>The owner's controls for the same things. Every route needs an admin session.</summary>
[ApiController]
[Route("api/v1/admin")]
[Authorize(Policy = AdminRequirement.PolicyName)]
public class SiteAdminController : ControllerBase
{
    private readonly ISiteService _site;

    public SiteAdminController(ISiteService site) => _site = site;

    private string Admin => User.FindFirstValue(ClaimTypes.Email) ?? User.FindFirstValue("email") ?? "unknown";

    [HttpGet("settings")]
    public async Task<ActionResult<SiteSettingsDto>> Settings(CancellationToken ct) => Ok(await _site.GetSettingsAsync(ct));

    [HttpPut("settings")]
    public async Task<ActionResult<SiteSettingsDto>> UpdateSettings(UpdateSiteSettingsRequest request, CancellationToken ct) =>
        Ok(await _site.UpdateSettingsAsync(request, Admin, ct));

    /// <summary>The note shown at the end of every post.</summary>
    [HttpGet("global-note")]
    public async Task<ActionResult<GlobalNoteDto>> GlobalNote(CancellationToken ct) => Ok(await _site.GetNoteAsync(ct));

    [HttpPut("global-note")]
    public async Task<ActionResult<GlobalNoteDto>> SaveGlobalNote(SaveGlobalNoteRequest request, CancellationToken ct) =>
        Ok(await _site.SaveNoteAsync(request, Admin, ct));

    [HttpGet("categories")]
    public async Task<ActionResult<IReadOnlyList<CategoryWithCountDto>>> Categories(CancellationToken ct) =>
        Ok(await _site.GetCategoriesAsync(ct));

    [HttpPost("categories")]
    public async Task<ActionResult<CategoryWithCountDto>> CreateCategory(SaveCategoryRequest request, CancellationToken ct) =>
        Ok(await _site.CreateCategoryAsync(request, Admin, ct));

    [HttpPut("categories/{id:guid}")]
    public async Task<ActionResult<CategoryWithCountDto>> RenameCategory(Guid id, SaveCategoryRequest request, CancellationToken ct) =>
        Ok(await _site.RenameCategoryAsync(id, request, Admin, ct));

    [HttpDelete("categories/{id:guid}")]
    public async Task<IActionResult> DeleteCategory(Guid id, CancellationToken ct)
    {
        await _site.DeleteCategoryAsync(id, Admin, ct);
        return NoContent();
    }
}
