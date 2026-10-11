using Articles.Application.Common;
using Articles.Application.Engagement;
using Articles.Application.Posts.Dtos;
using Articles.Application.Tags;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Articles.Api.Controllers;

[ApiController]
[Route("api/v1/tags")]
public class TagsController : ControllerBase
{
    private readonly ITagService _tags;
    private readonly IEngagementService _engagement;
    private readonly ICurrentUser _currentUser;

    public TagsController(ITagService tags, IEngagementService engagement, ICurrentUser currentUser)
    {
        _tags = tags;
        _engagement = engagement;
        _currentUser = currentUser;
    }

    /// <summary>Most-used topics, for the discovery sidebar and the empty-feed state.</summary>
    [HttpGet]
    [AllowAnonymous]
    public async Task<ActionResult<IReadOnlyList<TagDto>>> Popular([FromQuery] int limit = 20, CancellationToken ct = default) =>
        Ok(await _tags.GetPopularAsync(limit, ct));

    /// <summary>Type-ahead for the editor's tag picker.</summary>
    [HttpGet("suggest")]
    [AllowAnonymous]
    public async Task<ActionResult<IReadOnlyList<TagDto>>> Suggest([FromQuery] string q, CancellationToken ct) =>
        Ok(await _tags.SuggestAsync(q, 10, ct));

    [HttpGet("following")]
    [Authorize]
    public async Task<ActionResult<IReadOnlyList<TagDto>>> Following(CancellationToken ct) =>
        Ok(await _tags.GetFollowedAsync(_currentUser.RequireUserId(), ct));

    [HttpPost("{slug}/follow")]
    [Authorize]
    public async Task<ActionResult<ToggleResult>> ToggleFollow(string slug, CancellationToken ct) =>
        Ok(await _engagement.ToggleFollowTagAsync(slug, _currentUser.RequireUserId(), ct));
}
