using Articles.Domain.Entities;
using Articles.Domain.Enums;

namespace Articles.Domain.Interfaces;

public interface IReactionRepository
{
    Task<Reaction?> GetAsync(Guid postId, string visitorKey, ReactionKind kind, CancellationToken ct = default);
    Task<IReadOnlyList<ReactionKind>> GetKindsAsync(Guid postId, string visitorKey, CancellationToken ct = default);
    Task AddAsync(Reaction reaction, CancellationToken ct = default);
    void Remove(Reaction reaction);

    Task AddViewAsync(PostView view, CancellationToken ct = default);
    Task<bool> HasViewAsync(Guid postId, string visitorKey, int day, CancellationToken ct = default);

    /// <summary>Forgets who read what before the given day; only today's record is needed to avoid double counting.</summary>
    Task DeleteViewsBeforeAsync(int day, CancellationToken ct = default);

    /// <summary>
    /// Saves pending changes. Returns false, saving nothing, when another request for the same visitor
    /// got there first (two clicks arriving together), so the caller can report the state as it now is.
    /// </summary>
    Task<bool> TrySaveAsync(CancellationToken ct = default);
}
