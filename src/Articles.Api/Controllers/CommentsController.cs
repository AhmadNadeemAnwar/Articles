using Articles.Application.Comments;
using Articles.Application.Comments.Dtos;
using Articles.Application.Common;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Articles.Api.Controllers;

[ApiController]
[Route("api/v1/comments")]
[Authorize]
public class CommentsController : ControllerBase
{
    private readonly ICommentService _comments;
    private readonly ICurrentUser _currentUser;

    public CommentsController(ICommentService comments, ICurrentUser currentUser)
    {
        _comments = comments;
        _currentUser = currentUser;
    }

    [HttpPut("{id:guid}")]
    public async Task<ActionResult<CommentDto>> Update(Guid id, UpdateCommentRequest request, CancellationToken ct) =>
        Ok(await _comments.UpdateAsync(id, request, _currentUser.RequireUserId(), ct));

    /// <summary>Soft-deletes a comment. Allowed for the comment's author or the post's author.</summary>
    [HttpDelete("{id:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        await _comments.DeleteAsync(id, _currentUser.RequireUserId(), ct);
        return NoContent();
    }
}
