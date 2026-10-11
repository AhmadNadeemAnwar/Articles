using Articles.Domain.Entities;

namespace Articles.Domain.Interfaces;

/// <summary>Bookmarks and follows for signed-in accounts. Reactions from visitors live in <see cref="IReactionRepository"/>.</summary>
public interface IEngagementRepository
{
    Task<Bookmark?> GetBookmarkAsync(Guid postId, Guid userId, CancellationToken ct = default);
    Task AddBookmarkAsync(Bookmark bookmark, CancellationToken ct = default);
    void RemoveBookmark(Bookmark bookmark);

    Task<UserFollow?> GetUserFollowAsync(Guid followerId, Guid followeeId, CancellationToken ct = default);
    Task AddUserFollowAsync(UserFollow follow, CancellationToken ct = default);
    void RemoveUserFollow(UserFollow follow);

    Task<TagFollow?> GetTagFollowAsync(Guid userId, Guid tagId, CancellationToken ct = default);
    Task AddTagFollowAsync(TagFollow follow, CancellationToken ct = default);
    void RemoveTagFollow(TagFollow follow);

    Task<bool> HasBookmarkedAsync(Guid postId, Guid userId, CancellationToken ct = default);
}
