using Articles.Domain.Common;
using Articles.Domain.Entities;

namespace Articles.Domain.Interfaces;

public interface IPostRepository
{
    Task<Post?> GetByIdAsync(Guid id, CancellationToken ct = default);

    Task<Post?> GetBySlugAsync(string slug, CancellationToken ct = default);

    Task<bool> SlugExistsAsync(string slug, CancellationToken ct = default);

    /// <summary>
    /// Browse and full-text search over published posts. This is the single place that knows how
    /// search is implemented, so moving from SQL LIKE to Postgres tsvector touches only this method.
    /// </summary>
    Task<PagedResult<Post>> SearchAsync(PostQuery query, CancellationToken ct = default);

    /// <summary>Posts by the writers and tags a reader follows, newest first.</summary>
    Task<PagedResult<Post>> GetPersonalFeedAsync(Guid userId, int pageNumber, int pageSize, CancellationToken ct = default);

    Task<PagedResult<Post>> GetDraftsAsync(Guid authorId, int pageNumber, int pageSize, CancellationToken ct = default);

    Task<PagedResult<Post>> GetBookmarkedAsync(Guid userId, int pageNumber, int pageSize, CancellationToken ct = default);

    /// <summary>Tag-overlap based "more like this", excluding the source post.</summary>
    Task<IReadOnlyList<Post>> GetRelatedAsync(Guid postId, int limit, CancellationToken ct = default);

    Task AddAsync(Post post, CancellationToken ct = default);
    void Remove(Post post);
    Task AddRevisionAsync(PostRevision revision, CancellationToken ct = default);
    Task<IReadOnlyList<PostRevision>> GetRevisionsAsync(Guid postId, int limit, CancellationToken ct = default);
    Task<PostRevision?> GetRevisionAsync(Guid postId, Guid revisionId, CancellationToken ct = default);
    Task<DateTimeOffset?> GetLatestRevisionTimeAsync(Guid postId, CancellationToken ct = default);

    /// <summary>Removes all but the newest <paramref name="keep"/> saved revisions of a post.</summary>
    Task TrimRevisionsAsync(Guid postId, int keep, CancellationToken ct = default);
}
