using Articles.Application.Auth.Dtos;
using Articles.Application.Posts.Mapping;
using Articles.Application.Users.Dtos;
using Articles.Domain.Entities;
using Articles.Domain.Exceptions;
using Articles.Domain.Interfaces;

namespace Articles.Application.Users;

public sealed class UserService : IUserService
{
    private readonly IUserRepository _users;
    private readonly IEngagementRepository _engagement;
    private readonly IUnitOfWork _unitOfWork;

    public UserService(IUserRepository users, IEngagementRepository engagement, IUnitOfWork unitOfWork)
    {
        _users = users;
        _engagement = engagement;
        _unitOfWork = unitOfWork;
    }

    public async Task<ProfileDto> GetProfileAsync(string handle, Guid? viewerId, CancellationToken ct = default)
    {
        var user = await _users.GetByHandleAsync(handle.Trim().ToLowerInvariant(), ct)
            ?? throw new NotFoundException(nameof(User), handle);

        var isFollowing = viewerId is { } id && id != user.Id
            && await _engagement.GetUserFollowAsync(id, user.Id, ct) is not null;

        return new ProfileDto(
            user.Id,
            user.Handle,
            user.DisplayName,
            user.Bio,
            user.AvatarUrl,
            user.WebsiteUrl,
            user.CreatedAt,
            await _users.CountPublishedPostsAsync(user.Id, ct),
            await _users.CountFollowersAsync(user.Id, ct),
            await _users.CountFollowingAsync(user.Id, ct),
            isFollowing,
            viewerId == user.Id);
    }

    public async Task<CurrentUserDto> UpdateProfileAsync(Guid userId, UpdateProfileRequest request, CancellationToken ct = default)
    {
        var user = await _users.GetByIdAsync(userId, ct) ?? throw new NotFoundException(nameof(User), userId);

        user.UpdateProfile(request.DisplayName, request.Bio, request.AvatarUrl, request.WebsiteUrl);
        await _unitOfWork.SaveChangesAsync(ct);

        return user.ToCurrentUser();
    }
}
