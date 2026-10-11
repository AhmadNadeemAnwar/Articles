using Articles.Domain.Common;
using Articles.Domain.Entities;

namespace Articles.Domain.Interfaces;

public interface ISubscriberRepository
{
    Task<Subscriber?> GetByEmailAsync(string email, CancellationToken ct = default);
    Task<Subscriber?> GetAsync(Guid id, CancellationToken ct = default);
    Task AddAsync(Subscriber subscriber, CancellationToken ct = default);
    void Remove(Subscriber subscriber);

    /// <summary>Confirmed subscribers who have not yet been told about this post, oldest first, up to a limit.</summary>
    Task<IReadOnlyList<Subscriber>> GetUnnotifiedAsync(Guid postId, int limit, CancellationToken ct = default);
    Task<int> CountUnnotifiedAsync(Guid postId, CancellationToken ct = default);

    Task<int> CountAsync(SubscriberStatus status, CancellationToken ct = default);
    Task<PagedResult<Subscriber>> GetPageAsync(int pageNumber, int pageSize, CancellationToken ct = default);
}
