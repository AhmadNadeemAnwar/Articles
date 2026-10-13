using Articles.Domain.Entities;
using Articles.Domain.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace Articles.Infrastructure.Persistence.Repositories;

public sealed class TextImprovementRepository : ITextImprovementRepository
{
    private readonly AppDbContext _db;

    public TextImprovementRepository(AppDbContext db) => _db = db;

    public async Task AddAsync(TextImprovement improvement, CancellationToken ct = default) =>
        await _db.TextImprovements.AddAsync(improvement, ct);

    public async Task<IReadOnlyList<TextImprovement>> ListForPostAsync(Guid postId, int limit, CancellationToken ct = default) =>
        await _db.TextImprovements.AsNoTracking()
            .Where(t => t.PostId == postId)
            .OrderByDescending(t => t.CreatedAt)
            .Take(limit)
            .ToListAsync(ct);

    public Task<bool> PostExistsAsync(Guid postId, CancellationToken ct = default) =>
        _db.Posts.AsNoTracking().AnyAsync(p => p.Id == postId, ct);
}
