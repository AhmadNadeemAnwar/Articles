using Articles.Api.Contracts;
using Articles.Application.Auth.Dtos;
using Articles.Application.Common;
using Articles.Application.Engagement;
using Articles.Application.Posts;
using Articles.Application.Posts.Dtos;
using Articles.Application.Users;
using Articles.Application.Users.Dtos;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Articles.Api.Controllers;

[ApiController]
[Route("api/v1/users")]
public class UsersController : ControllerBase
{
    private readonly IUserService _users;
    private readonly IPostService _posts;
    private readonly IEngagementService _engagement;
    private readonly ICurrentUser _currentUser;

    public UsersController(IUserService users, IPostService posts, IEngagementService engagement, ICurrentUser currentUser)
    {
        _users = users;
        _posts = posts;
        _engagement = engagement;
        _currentUser = currentUser;
    }

    [HttpGet("{handle}")]
    [AllowAnonymous]
    [ProducesResponseType(typeof(ProfileDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ProfileDto>> GetProfile(string handle, CancellationToken ct) =>
        Ok(await _users.GetProfileAsync(handle, _currentUser.UserId, ct));

    /// <summary>The writer's published posts, newest first.</summary>
    [HttpGet("{handle}/posts")]
    [AllowAnonymous]
    public async Task<ActionResult<PagedResponse<PostSummaryDto>>> GetPosts(
        string handle, [FromQuery] int pageNumber = 1, [FromQuery] int pageSize = 20, CancellationToken ct = default)
    {
        var parameters = new PostQueryParameters { Author = handle, PageNumber = pageNumber, PageSize = pageSize };
        return Ok(PagedResponse<PostSummaryDto>.From(await _posts.SearchAsync(parameters, ct)));
    }

    [HttpPut("me")]
    [Authorize]
    public async Task<ActionResult<CurrentUserDto>> UpdateProfile(UpdateProfileRequest request, CancellationToken ct) =>
        Ok(await _users.UpdateProfileAsync(_currentUser.RequireUserId(), request, ct));

    [HttpPost("{handle}/follow")]
    [Authorize]
    public async Task<ActionResult<ToggleResult>> ToggleFollow(string handle, CancellationToken ct) =>
        Ok(await _engagement.ToggleFollowUserAsync(handle, _currentUser.RequireUserId(), ct));
}
