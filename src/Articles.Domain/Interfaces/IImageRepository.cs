using Articles.Domain.Entities;

namespace Articles.Domain.Interfaces;

public interface IImageRepository
{
    Task AddAsync(StoredImage image, CancellationToken ct = default);
    Task<StoredImage?> GetAsync(Guid id, CancellationToken ct = default);

    /// <summary>Bytes held by every stored image, used to stay inside the database's free allowance.</summary>
    Task<long> GetTotalBytesAsync(CancellationToken ct = default);
}
