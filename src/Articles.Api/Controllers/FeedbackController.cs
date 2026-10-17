using System.Security.Claims;
using Articles.Api.Common;
using Articles.Api.Contracts;
using Articles.Application.Engagement;
using Articles.Application.Posts;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Articles.Api.Controllers;

/// <summary>What readers think and how many there are: anonymous feedback (sent by anyone, read and deleted by the owner) and daily views.</summary>
[ApiController]
[Route("api/v1")]
public class FeedbackController : ControllerBase
{
    private readonly IFeedbackService _feedback;
    private readonly IViewStatsService _views;

    public FeedbackController(IFeedbackService feedback, IViewStatsService views)
    {
        _feedback = feedback;
        _views = views;
    }

    private string Admin => User.FindFirstValue(ClaimTypes.Email) ?? User.FindFirstValue("email") ?? "unknown";

    /// <summary>A reader's note on a published post. Limited per reader so it cannot be used to flood the list.</summary>
    [HttpPost("posts/{id:guid}/feedback")]
    [AllowAnonymous]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Submit(Guid id, SubmitFeedbackRequest request, CancellationToken ct)
    {
        await _feedback.SubmitAsync(id, request, ct);
        return NoContent();
    }

    [HttpGet("admin/feedback")]
    [Authorize(Policy = AdminRequirement.PolicyName)]
    public async Task<ActionResult<PagedResponse<FeedbackDto>>> List([FromQuery] int pageNumber = 1, [FromQuery] int pageSize = 25, CancellationToken ct = default)
    {
        var page = await _feedback.GetPageAsync(Math.Max(1, pageNumber), Math.Clamp(pageSize, 1, 100), ct);
        return Ok(PagedResponse<FeedbackDto>.From(page));
    }

    [HttpDelete("admin/feedback/{id:guid}")]
    [Authorize(Policy = AdminRequirement.PolicyName)]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        await _feedback.DeleteAsync(id, Admin, ct);
        return NoContent();
    }

    /// <summary>Readers per day for one post or all of them, with the coarse facts about them. Counts only.</summary>
    [HttpGet("admin/views")]
    [Authorize(Policy = AdminRequirement.PolicyName)]
    public async Task<ActionResult<ViewStatsDto>> Views([FromQuery] Guid? postId = null, [FromQuery] int days = 30, CancellationToken ct = default) =>
        Ok(await _views.GetAsync(postId, days, ct));
}
