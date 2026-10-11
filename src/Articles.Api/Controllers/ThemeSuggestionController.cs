using System.Security.Claims;
using Articles.Api.Common;
using Articles.Application.Site;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Articles.Api.Controllers;

/// <summary>"Amaze with AI": the AI proposes colours for the custom theme. Admin only. Nothing is saved here; the owner keeps them with the usual theme save.</summary>
[ApiController]
[Route("api/v1/admin/settings/ai-colours")]
[Authorize(Policy = AdminRequirement.PolicyName)]
public class ThemeSuggestionController : ControllerBase
{
    private readonly IThemeSuggestionService _suggestions;

    public ThemeSuggestionController(IThemeSuggestionService suggestions) => _suggestions = suggestions;

    private string Admin => User.FindFirstValue(ClaimTypes.Email) ?? User.FindFirstValue("email") ?? "unknown";

    [HttpGet]
    public async Task<ActionResult<ThemeAiStatus>> Status(CancellationToken ct) => Ok(await _suggestions.GetStatusAsync(ct));

    [HttpPost]
    public async Task<ActionResult<ThemeSuggestion>> Suggest(CancellationToken ct) => Ok(await _suggestions.SuggestAsync(Admin, ct));
}
