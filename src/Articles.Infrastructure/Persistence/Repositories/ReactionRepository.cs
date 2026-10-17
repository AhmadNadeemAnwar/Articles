using Articles.Domain.Entities;
using Articles.Domain.Enums;
using Articles.Domain.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace Articles.Infrastructure.Persistence.Repositories;

public sealed class ReactionRepository : IReactionRepository
{
    private readonly AppDbContext _db;

    public ReactionRepository(AppDbContext db) => _db = db;

    public Task<Reaction?> GetAsync(Guid postId, string visitorKey, ReactionKind kind, CancellationToken ct = default) =>
        _db.Reactions.FirstOrDefaultAsync(r => r.PostId == postId && r.VisitorKey == visitorKey && r.Kind == kind, ct);

    public async Task<IReadOnlyList<ReactionKind>> GetKindsAsync(Guid postId, string visitorKey, CancellationToken ct = default) =>
        await _db.Reactions.AsNoTracking()
            .Where(r => r.PostId == postId && r.VisitorKey == visitorKey)
            .Select(r => r.Kind)
            .ToListAsync(ct);

    public async Task AddAsync(Reaction reaction, CancellationToken ct = default) =>
        await _db.Reactions.AddAsync(reaction, ct);

    public void Remove(Reaction reaction) => _db.Reactions.Remove(reaction);

    public async Task AddViewAsync(PostView view, CancellationToken ct = default) =>
        await _db.PostViews.AddAsync(view, ct);

    public Task<bool> HasViewAsync(Guid postId, string visitorKey, int day, CancellationToken ct = default) =>
        _db.PostViews.AnyAsync(v => v.PostId == postId && v.VisitorKey == visitorKey && v.Day == day, ct);

    public Task<bool> HasViewBeforeAsync(Guid postId, string visitorKey, int day, CancellationToken ct = default) =>
        _db.PostViews.AnyAsync(v => v.PostId == postId && v.VisitorKey == visitorKey && v.Day < day, ct);

    public Task<PostViewStat?> FindStatAsync(Guid postId, int day, string country, string device, string source, bool returning, CancellationToken ct = default) =>
        _db.PostViewStats.FirstOrDefaultAsync(s => s.PostId == postId && s.Day == day && s.Country == country && s.Device == device && s.Source == source && s.Returning == returning, ct);

    public async Task AddStatAsync(PostViewStat stat, CancellationToken ct = default) =>
        await _db.PostViewStats.AddAsync(stat, ct);

    public async Task<IReadOnlyList<PostViewStat>> GetStatsAsync(Guid? postId, int fromDay, CancellationToken ct = default) =>
        await _db.PostViewStats.AsNoTracking()
            .Where(s => s.Day >= fromDay && (postId == null || s.PostId == postId))
            .ToListAsync(ct);

    public Task DeleteViewsBeforeAsync(int day, CancellationToken ct = default) =>
        _db.PostViews.Where(v => v.Day < day).ExecuteDeleteAsync(ct);

    public async Task<bool> TrySaveAsync(CancellationToken ct = default)
    {
        try
        {
            await _db.SaveChangesAsync(ct);
            return true;
        }
        catch (DbUpdateException)
        {
            // A second request for the same visitor inserted (or deleted) the same row first. Drop
            // what this request was going to write so the context can be read from again.
            _db.ChangeTracker.Clear();
            return false;
        }
    }
}
