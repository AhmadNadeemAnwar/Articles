using Articles.Domain.Entities;
using Articles.Domain.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace Articles.Infrastructure.Persistence.Repositories;

public sealed class ImageRepository : IImageRepository
{
    private readonly AppDbContext _db;

    public ImageRepository(AppDbContext db) => _db = db;

    public async Task AddAsync(StoredImage image, CancellationToken ct = default) =>
        await _db.Images.AddAsync(image, ct);

    public Task<StoredImage?> GetAsync(Guid id, CancellationToken ct = default) =>
        _db.Images.AsNoTracking().FirstOrDefaultAsync(i => i.Id == id, ct);

    // Sums the stored sizes rather than the bytes themselves, so no image is read to answer this.
    public async Task<long> GetTotalBytesAsync(CancellationToken ct = default) =>
        await _db.Images.SumAsync(i => (long)i.Size, ct);
}
