using Articles.Domain.Entities;

namespace Articles.Domain.Interfaces;

public interface ICommentRepository
{
    Task<Comment?> GetByIdAsync(Guid id, CancellationToken ct = default);
    /// <summary>Top-level comments for a post with their replies eager-loaded, oldest first.</summary>
    Task<IReadOnlyList<Comment>> GetThreadAsync(Guid postId, CancellationToken ct = default);
    Task AddAsync(Comment comment, CancellationToken ct = default);
}
