namespace Articles.Domain.Interfaces;

/// <summary>
/// Commits everything tracked in the current request as one transaction. Repositories stage
/// changes; only the service layer decides when they are persisted.
/// </summary>
public interface IUnitOfWork
{
    Task<int> SaveChangesAsync(CancellationToken ct = default);
}
