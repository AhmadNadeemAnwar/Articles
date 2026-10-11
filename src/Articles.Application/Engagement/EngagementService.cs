using Articles.Domain.Entities;
using Articles.Domain.Exceptions;
using Articles.Domain.Interfaces;

namespace Articles.Application.Engagement;

public sealed record ToggleResult(bool IsActive);

public interface IEngagementService
{
    Task<ToggleResult> ToggleBookmarkAsync(Guid postId, Guid userId, CancellationToken ct = default);
    Task<ToggleResult> ToggleFollowUserAsync(string handle, Guid followerId, CancellationToken ct = default);
    Task<ToggleResult> ToggleFollowTagAsync(string tagSlug, Guid userId, CancellationToken ct = default);
}

public sealed class EngagementService : IEngagementService
{
    private readonly IEngagementRepository _engagement;
    private readonly IPostRepository _posts;
    private readonly IUserRepository _users;
    private readonly ITagRepository _tags;
    private readonly IUnitOfWork _unitOfWork;

    public EngagementService(
        IEngagementRepository engagement,
        IPostRepository posts,
        IUserRepository users,
        ITagRepository tags,
        IUnitOfWork unitOfWork)
    {
        _engagement = engagement;
        _posts = posts;
        _users = users;
        _tags = tags;
        _unitOfWork = unitOfWork;
    }

    public async Task<ToggleResult> ToggleBookmarkAsync(Guid postId, Guid userId, CancellationToken ct = default)
    {
        var post = await _posts.GetByIdAsync(postId, ct) ?? throw new NotFoundException(nameof(Post), postId);
        if (!post.IsVisibleTo(userId)) throw new NotFoundException(nameof(Post), postId);

        var bookmark = await _engagement.GetBookmarkAsync(postId, userId, ct);

        if (bookmark is null)
        {
            await _engagement.AddBookmarkAsync(new Bookmark(postId, userId), ct);
            await _unitOfWork.SaveChangesAsync(ct);
            return new ToggleResult(true);
        }

        _engagement.RemoveBookmark(bookmark);
        await _unitOfWork.SaveChangesAsync(ct);
        return new ToggleResult(false);
    }

    public async Task<ToggleResult> ToggleFollowUserAsync(string handle, Guid followerId, CancellationToken ct = default)
    {
        var target = await _users.GetByHandleAsync(handle.Trim().ToLowerInvariant(), ct)
            ?? throw new NotFoundException(nameof(User), handle);

        var follow = await _engagement.GetUserFollowAsync(followerId, target.Id, ct);

        if (follow is null)
        {
            await _engagement.AddUserFollowAsync(new UserFollow(followerId, target.Id), ct);
            await _unitOfWork.SaveChangesAsync(ct);
            return new ToggleResult(true);
        }

        _engagement.RemoveUserFollow(follow);
        await _unitOfWork.SaveChangesAsync(ct);
        return new ToggleResult(false);
    }

    public async Task<ToggleResult> ToggleFollowTagAsync(string tagSlug, Guid userId, CancellationToken ct = default)
    {
        var tag = await _tags.GetBySlugAsync(tagSlug.Trim().ToLowerInvariant(), ct)
            ?? throw new NotFoundException(nameof(Tag), tagSlug);

        var follow = await _engagement.GetTagFollowAsync(userId, tag.Id, ct);

        if (follow is null)
        {
            await _engagement.AddTagFollowAsync(new TagFollow(userId, tag.Id), ct);
            await _unitOfWork.SaveChangesAsync(ct);
            return new ToggleResult(true);
        }

        _engagement.RemoveTagFollow(follow);
        await _unitOfWork.SaveChangesAsync(ct);
        return new ToggleResult(false);
    }
}
