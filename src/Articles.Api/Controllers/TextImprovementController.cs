using System.Security.Claims;
using Articles.Api.Common;
using Articles.Application.Posts;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Articles.Api.Controllers;

/// <summary>"Improve text with AI" for the post editor. Admin only.</summary>
[ApiController]
[Route("api/v1/admin/text-improvement")]
[Authorize(Policy = AdminRequirement.PolicyName)]
public class TextImprovementController : ControllerBase
{
    private readonly ITextImprovementService _service;

    public TextImprovementController(ITextImprovementService service) => _service = service;

    private string Admin => User.FindFirstValue(ClaimTypes.Email) ?? User.FindFirstValue("email") ?? "unknown";

    /// <summary>Whether the AI is set up, and how many goes are left today.</summary>
    [HttpGet]
    public async Task<ActionResult<TextImprovementStatus>> Status(CancellationToken ct) => Ok(await _service.GetStatusAsync(ct));

    /// <summary>The lines reworded. Nothing is saved: the editor shows them first and the writer decides.</summary>
    [HttpPost("improve")]
    public async Task<ActionResult<ImprovedTextDto>> Improve(ImproveTextRequest request, CancellationToken ct) =>
        Ok(await _service.ImproveAsync(Admin, request, ct));
}
