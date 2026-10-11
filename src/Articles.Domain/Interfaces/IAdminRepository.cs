using Articles.Domain.Common;
using Articles.Domain.Entities;
using Articles.Domain.Enums;

namespace Articles.Domain.Interfaces;

/// <summary>Site-wide reads and bulk edits that only the admin portal needs.</summary>
public interface IAdminRepository
{
    Task<AdminStats> GetStatsAsync(DateTimeOffset publishedSince, CancellationToken ct = default);

    /// <summary>Every post regardless of status or author, most recently changed first.</summary>
    /// <param name="categoryId">Only posts in this category.</param>
    /// <param name="uncategorised">Only posts in no category (ignored when <paramref name="categoryId"/> is given).</param>
    Task<PagedResult<Post>> SearchPostsAsync(PostStatus? status, string? search, int pageNumber, int pageSize, Guid? categoryId = null, bool uncategorised = false, CancellationToken ct = default);

    /// <summary>Comments across all posts, newest first, with their post and author loaded.</summary>
    Task<PagedResult<Comment>> GetCommentsAsync(int pageNumber, int pageSize, CancellationToken ct = default);

    Task<IReadOnlyList<AdminTagRow>> GetTagsAsync(CancellationToken ct = default);
    Task<Tag?> GetTagAsync(Guid id, CancellationToken ct = default);

    /// <summary>
    /// Moves every post and follower from <paramref name="sourceId"/> to <paramref name="targetId"/>
    /// (skipping any that already have the target) and removes the source. Changes are staged, not saved.
    /// </summary>
    Task MergeTagsAsync(Guid sourceId, Guid targetId, CancellationToken ct = default);

    void RemoveTag(Tag tag);

    /// <summary>Every post with its author and topics, oldest first, for a full backup.</summary>
    Task<IReadOnlyList<Post>> GetAllPostsAsync(CancellationToken ct = default);
}
