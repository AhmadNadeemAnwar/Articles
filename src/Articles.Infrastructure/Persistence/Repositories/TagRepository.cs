using Articles.Domain.Entities;
using Articles.Domain.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace Articles.Infrastructure.Persistence.Repositories;

public class TagRepository : ITagRepository
{
    private readonly AppDbContext _db;

    public TagRepository(AppDbContext db) => _db = db;

    public async Task<Tag?> GetBySlugAsync(string slug, CancellationToken ct = default) =>
        await _db.Tags.FirstOrDefaultAsync(t => t.Slug == slug, ct);

    public async Task<IReadOnlyList<Tag>> GetBySlugsAsync(IEnumerable<string> slugs, CancellationToken ct = default)
    {
        var list = slugs.Distinct().ToList();
        if (list.Count == 0) return [];

        // Tracked, not AsNoTracking: callers attach these to posts and adjust their post counts.
        return await _db.Tags.Where(t => list.Contains(t.Slug)).ToListAsync(ct);
    }

    public async Task<IReadOnlyList<Tag>> GetPopularAsync(int limit, CancellationToken ct = default) =>
        await _db.Tags.AsNoTracking()
            .Where(t => t.PostCount > 0)
            .OrderByDescending(t => t.PostCount)
            .ThenBy(t => t.Name)
            .Take(limit)
            .ToListAsync(ct);

    public async Task<IReadOnlyList<Tag>> SearchAsync(string term, int limit, CancellationToken ct = default) =>
        await _db.Tags.AsNoTracking()
            .Where(t => t.Name.ToLower().Contains(term.ToLower()))
            .OrderByDescending(t => t.PostCount)
            .Take(limit)
            .ToListAsync(ct);

    public async Task<IReadOnlyList<Tag>> GetFollowedByAsync(Guid userId, CancellationToken ct = default) =>
        await _db.TagFollows.AsNoTracking()
            .Where(f => f.UserId == userId)
            .OrderBy(f => f.Tag.Name)
            .Select(f => f.Tag)
            .ToListAsync(ct);

    public async Task AddAsync(Tag tag, CancellationToken ct = default) =>
        await _db.Tags.AddAsync(tag, ct);
}
