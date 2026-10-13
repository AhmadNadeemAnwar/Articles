using Articles.Domain.Entities;

namespace Articles.Domain.Interfaces;

public interface ITextImprovementRepository
{
    Task AddAsync(TextImprovement improvement, CancellationToken ct = default);

    /// <summary>The newest first, at most <paramref name="limit"/> of them.</summary>
    Task<IReadOnlyList<TextImprovement>> ListForPostAsync(Guid postId, int limit, CancellationToken ct = default);

    Task<bool> PostExistsAsync(Guid postId, CancellationToken ct = default);
}
