using Articles.Api.Contracts;
using Articles.Application.Comments;
using Articles.Application.Comments.Dtos;
using Articles.Application.Common;
using Articles.Application.Engagement;
using Articles.Application.Posts;
using Articles.Application.Posts.Dtos;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Articles.Api.Controllers;

[ApiController]
[Route("api/v1/posts")]
public class PostsController : ControllerBase
{
    private readonly IPostService _posts;
    private readonly ICommentService _comments;
    private readonly IEngagementService _engagement;
    private readonly IReactionService _reactions;
    private readonly ICurrentUser _currentUser;

    public PostsController(
        IPostService posts,
        ICommentService comments,
        IEngagementService engagement,
        IReactionService reactions,
        ICurrentUser currentUser)
    {
        _posts = posts;
        _comments = comments;
        _engagement = engagement;
        _reactions = reactions;
        _currentUser = currentUser;
    }

    /// <summary>The random id a reader's browser made up for itself. Readers have no accounts, so this is all that tells two of them apart.</summary>
    private string? VisitorId => Request.Headers["X-Visitor-Id"].ToString();

    /// <summary>
    /// Browse and search published posts. Supports free text (<c>q</c>), tag, author,
    /// sort order and paging.
    /// </summary>
    [HttpGet]
    [AllowAnonymous]
    [ProducesResponseType(typeof(PagedResponse<PostSummaryDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<PagedResponse<PostSummaryDto>>> Search([FromQuery] PostQueryParameters parameters, CancellationToken ct) =>
        Ok(PagedResponse<PostSummaryDto>.From(await _posts.SearchAsync(parameters, ct)));

    /// <summary>Posts from the writers and topics the signed-in reader follows.</summary>
    [HttpGet("feed")]
    [Authorize]
    public async Task<ActionResult<PagedResponse<PostSummaryDto>>> Feed(
        [FromQuery] int pageNumber = 1, [FromQuery] int pageSize = 20, CancellationToken ct = default)
    {
        var result = await _posts.GetPersonalFeedAsync(_currentUser.RequireUserId(), Math.Max(1, pageNumber), Math.Clamp(pageSize, 1, 50), ct);
        return Ok(PagedResponse<PostSummaryDto>.From(result));
    }

    [HttpGet("drafts")]
    [Authorize]
    public async Task<ActionResult<PagedResponse<PostSummaryDto>>> Drafts(
        [FromQuery] int pageNumber = 1, [FromQuery] int pageSize = 20, CancellationToken ct = default)
    {
        var result = await _posts.GetDraftsAsync(_currentUser.RequireUserId(), Math.Max(1, pageNumber), Math.Clamp(pageSize, 1, 50), ct);
        return Ok(PagedResponse<PostSummaryDto>.From(result));
    }

    [HttpGet("bookmarks")]
    [Authorize]
    public async Task<ActionResult<PagedResponse<PostSummaryDto>>> Bookmarks(
        [FromQuery] int pageNumber = 1, [FromQuery] int pageSize = 20, CancellationToken ct = default)
    {
        var result = await _posts.GetBookmarksAsync(_currentUser.RequireUserId(), Math.Max(1, pageNumber), Math.Clamp(pageSize, 1, 50), ct);
        return Ok(PagedResponse<PostSummaryDto>.From(result));
    }

    /// <summary>Reads a post by its canonical slug. Anonymous readers are allowed.</summary>
    [HttpGet("{slug}")]
    [AllowAnonymous]
    [ProducesResponseType(typeof(PostDetailDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<PostDetailDto>> GetBySlug(string slug, CancellationToken ct) =>
        Ok(await _posts.GetBySlugAsync(slug, _currentUser.UserId, ct));

    /// <summary>Loads a post by id for editing, including unpublished drafts.</summary>
    [HttpGet("{id:guid}/edit")]
    [Authorize]
    public async Task<ActionResult<PostDetailDto>> GetForEdit(Guid id, CancellationToken ct) =>
        Ok(await _posts.GetForEditAsync(id, _currentUser.RequireUserId(), ct));

    [HttpGet("{id:guid}/related")]
    [AllowAnonymous]
    public async Task<ActionResult<IReadOnlyList<PostSummaryDto>>> Related(Guid id, [FromQuery] int limit = 4, CancellationToken ct = default) =>
        Ok(await _posts.GetRelatedAsync(id, Math.Clamp(limit, 1, 10), ct));

    [HttpGet("{id:guid}/revisions")]
    [Authorize]
    public async Task<ActionResult<IReadOnlyList<PostRevisionDto>>> Revisions(Guid id, CancellationToken ct) =>
        Ok(await _posts.GetRevisionsAsync(id, _currentUser.RequireUserId(), PostService.MaxRevisionsPerPost, ct));

    /// <summary>One saved revision with its body, so an earlier version can be put back into the editor.</summary>
    [HttpGet("{id:guid}/revisions/{revisionId:guid}")]
    [Authorize]
    public async Task<ActionResult<PostRevisionDetailDto>> Revision(Guid id, Guid revisionId, CancellationToken ct) =>
        Ok(await _posts.GetRevisionAsync(id, revisionId, _currentUser.RequireUserId(), ct));

    [HttpPost]
    [Authorize]
    [ProducesResponseType(typeof(PostDetailDto), StatusCodes.Status201Created)]
    public async Task<ActionResult<PostDetailDto>> Create(CreatePostRequest request, CancellationToken ct)
    {
        var post = await _posts.CreateDraftAsync(request, _currentUser.RequireUserId(), ct);
        return CreatedAtAction(nameof(GetForEdit), new { id = post.Id }, post);
    }

    /// <summary>Saves an edit. The client calls this on autosave, which also writes a revision.</summary>
    [HttpPut("{id:guid}")]
    [Authorize]
    public async Task<ActionResult<PostDetailDto>> Update(Guid id, UpdatePostRequest request, CancellationToken ct) =>
        Ok(await _posts.UpdateAsync(id, request, _currentUser.RequireUserId(), ct));

    [HttpPost("{id:guid}/publish")]
    [Authorize]
    public async Task<ActionResult<PostDetailDto>> Publish(Guid id, CancellationToken ct) =>
        Ok(await _posts.PublishAsync(id, _currentUser.RequireUserId(), ct));

    [HttpPost("{id:guid}/unpublish")]
    [Authorize]
    public async Task<ActionResult<PostDetailDto>> Unpublish(Guid id, CancellationToken ct) =>
        Ok(await _posts.UnpublishAsync(id, _currentUser.RequireUserId(), ct));

    /// <summary>Moves one of your posts to Draft, Published or Inactive.</summary>
    [HttpPost("{id:guid}/status")]
    [Authorize]
    public async Task<ActionResult<PostDetailDto>> SetStatus(Guid id, SetPostStatusRequest request, CancellationToken ct) =>
        Ok(await _posts.SetStatusAsync(id, request.Status, _currentUser.RequireUserId(), ct));

    [HttpDelete("{id:guid}")]
    [Authorize]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        await _posts.DeleteAsync(id, _currentUser.RequireUserId(), ct);
        return NoContent();
    }

    /// <summary>A post's reaction totals, and which ones this visitor has given. No account needed.</summary>
    [HttpGet("{id:guid}/reactions")]
    [AllowAnonymous]
    public async Task<ActionResult<ReactionState>> Reactions(Guid id, CancellationToken ct) =>
        Ok(await _reactions.GetAsync(id, VisitorId, ct));

    /// <summary>Gives a reaction (clap or insightful), or takes it back if this visitor already gave it.</summary>
    [HttpPost("{id:guid}/reactions/{kind}")]
    [AllowAnonymous]
    public async Task<ActionResult<ReactionState>> ToggleReaction(Guid id, string kind, CancellationToken ct) =>
        Ok(await _reactions.ToggleAsync(id, VisitorId, kind, ct));

    /// <summary>Reported by a reader's browser once a post is on screen. Counted at most once per visitor per day.</summary>
    [HttpPost("{id:guid}/view")]
    [AllowAnonymous]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> RecordView(Guid id, CancellationToken ct)
    {
        await _reactions.RecordViewAsync(id, VisitorId, Request.Headers.UserAgent.ToString(), _currentUser.UserId, ct);
        return NoContent();
    }

    [HttpPost("{id:guid}/bookmark")]
    [Authorize]
    public async Task<ActionResult<ToggleResult>> ToggleBookmark(Guid id, CancellationToken ct) =>
        Ok(await _engagement.ToggleBookmarkAsync(id, _currentUser.RequireUserId(), ct));

    [HttpGet("{id:guid}/comments")]
    [AllowAnonymous]
    public async Task<ActionResult<IReadOnlyList<CommentDto>>> GetComments(Guid id, CancellationToken ct) =>
        Ok(await _comments.GetThreadAsync(id, ct));

    [HttpPost("{id:guid}/comments")]
    [Authorize]
    [ProducesResponseType(typeof(CommentDto), StatusCodes.Status201Created)]
    public async Task<ActionResult<CommentDto>> AddComment(Guid id, CreateCommentRequest request, CancellationToken ct)
    {
        var comment = await _comments.AddAsync(id, request, _currentUser.RequireUserId(), ct);
        return Created($"/api/v1/posts/{id}/comments/{comment.Id}", comment);
    }
}
