using Articles.Domain.Common;
using Articles.Domain.Entities;
using Articles.Domain.Enums;
using Articles.Domain.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace Articles.Infrastructure.Persistence.Repositories;

public sealed class FeedbackRepository : IFeedbackRepository
{
    private readonly AppDbContext _db;

    public FeedbackRepository(AppDbContext db) => _db = db;

    public async Task AddAsync(ReaderFeedback feedback, CancellationToken ct = default) =>
        await _db.ReaderFeedback.AddAsync(feedback, ct);

    public async Task<PagedResult<ReaderFeedback>> GetPageAsync(int pageNumber, int pageSize, CancellationToken ct = default)
    {
        var query = _db.ReaderFeedback.AsNoTracking();
        var total = await query.CountAsync(ct);
        var items = await query.Include(f => f.Post)
            .OrderByDescending(f => f.CreatedAt)
            .Skip((pageNumber - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(ct);
        return new PagedResult<ReaderFeedback>(items, pageNumber, pageSize, total);
    }

    public Task<ReaderFeedback?> GetAsync(Guid id, CancellationToken ct = default) =>
        _db.ReaderFeedback.Include(f => f.Post).FirstOrDefaultAsync(f => f.Id == id, ct);

    public void Remove(ReaderFeedback feedback) => _db.ReaderFeedback.Remove(feedback);

    public Task<bool> IsPublishedAsync(Guid postId, CancellationToken ct = default) =>
        _db.Posts.AsNoTracking().AnyAsync(p => p.Id == postId && p.Status == PostStatus.Published, ct);
}
