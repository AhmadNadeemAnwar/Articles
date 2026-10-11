using System.Security.Claims;
using Articles.Api.Common;
using Articles.Api.Contracts;
using Articles.Application.Subscriptions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Articles.Api.Controllers;

/// <summary>What a reader can do about email: ask to subscribe, confirm, and stop. No account involved.</summary>
[ApiController]
[Route("api/v1/subscribers")]
[AllowAnonymous]
public class SubscribersController : ControllerBase
{
    private readonly ISubscriptionService _subscriptions;

    public SubscribersController(ISubscriptionService subscriptions) => _subscriptions = subscriptions;

    /// <summary>
    /// Starts a subscription by sending a confirmation email. The answer is the same whether the
    /// address is new, waiting or already subscribed, so it cannot be used to find out who reads the site.
    /// </summary>
    [HttpPost]
    [ProducesResponseType(StatusCodes.Status202Accepted)]
    public async Task<IActionResult> Subscribe(SubscribeRequest request, CancellationToken ct)
    {
        await _subscriptions.SubscribeAsync(request, ct);
        return Accepted();
    }

    [HttpPost("confirm")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Confirm(TokenRequest request, CancellationToken ct)
    {
        await _subscriptions.ConfirmAsync(request.Token, ct);
        return NoContent();
    }

    [HttpPost("unsubscribe")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Unsubscribe(TokenRequest request, CancellationToken ct)
    {
        await _subscriptions.UnsubscribeAsync(request.Token, ct);
        return NoContent();
    }
}

/// <summary>The owner's view of subscribers, and the button that tells them about a new post.</summary>
[ApiController]
[Route("api/v1/admin")]
[Authorize(Policy = AdminRequirement.PolicyName)]
public class SubscribersAdminController : ControllerBase
{
    private readonly ISubscriptionService _subscriptions;

    public SubscribersAdminController(ISubscriptionService subscriptions) => _subscriptions = subscriptions;

    private string Admin => User.FindFirstValue(ClaimTypes.Email) ?? User.FindFirstValue("email") ?? "unknown";

    [HttpGet("subscribers/summary")]
    public async Task<ActionResult<SubscribersSummaryDto>> Summary(CancellationToken ct) => Ok(await _subscriptions.GetSummaryAsync(ct));

    [HttpGet("subscribers")]
    public async Task<ActionResult<PagedResponse<SubscriberDto>>> List([FromQuery] int pageNumber = 1, [FromQuery] int pageSize = 50, CancellationToken ct = default) =>
        Ok(PagedResponse<SubscriberDto>.From(await _subscriptions.GetPageAsync(Math.Max(1, pageNumber), Math.Clamp(pageSize, 1, 100), ct)));

    [HttpDelete("subscribers/{id:guid}")]
    public async Task<IActionResult> Remove(Guid id, CancellationToken ct)
    {
        await _subscriptions.RemoveAsync(id, Admin, ct);
        return NoContent();
    }

    /// <summary>How many confirmed subscribers have not yet been told about this post.</summary>
    [HttpGet("posts/{id:guid}/notify")]
    public async Task<ActionResult<object>> Waiting(Guid id, CancellationToken ct) =>
        Ok(new { waiting = await _subscriptions.CountWaitingAsync(id, ct) });

    /// <summary>Emails subscribers about a published post. Running it again only reaches those not yet told.</summary>
    [HttpPost("posts/{id:guid}/notify")]
    public async Task<ActionResult<NotifyResultDto>> Notify(Guid id, CancellationToken ct) =>
        Ok(await _subscriptions.NotifyAsync(id, Admin, ct));
}
