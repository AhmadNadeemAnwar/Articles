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

    /// <summary>Whether this reader opened the post on any earlier day still on record.</summary>
    Task<bool> HasViewBeforeAsync(Guid postId, string visitorKey, int day, CancellationToken ct = default);

    /// <summary>
    /// Forgets who read what before the given day. Kept long enough to tell a returning reader from a new one, and no
    /// longer: the record is only a scrambled id, a post and a day.
    /// </summary>
    Task DeleteViewsBeforeAsync(int day, CancellationToken ct = default);

    /// <summary>The day's count for this combination of facts, tracked so it can be added to, or null if there is none yet.</summary>
    Task<PostViewStat?> FindStatAsync(Guid postId, int day, string country, string device, string source, bool returning, CancellationToken ct = default);
    Task AddStatAsync(PostViewStat stat, CancellationToken ct = default);

    /// <summary>Every reader ever counted on any post: the running total each post keeps, not the daily figures.</summary>
    Task<long> GetAllTimeViewsAsync(CancellationToken ct = default);

    /// <summary>Every count from <paramref name="fromDay"/> on, for one post or (with null) all of them.</summary>
    Task<IReadOnlyList<PostViewStat>> GetStatsAsync(Guid? postId, int fromDay, CancellationToken ct = default);

    /// <summary>
    /// Saves pending changes. Returns false, saving nothing, when another request for the same visitor
    /// got there first (two clicks arriving together), so the caller can report the state as it now is.
    /// </summary>
    Task<bool> TrySaveAsync(CancellationToken ct = default);
}
