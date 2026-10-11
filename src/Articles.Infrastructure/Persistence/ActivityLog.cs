using Articles.Application.Admin;
using Articles.Domain.Common;
using Articles.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.Extensions.Logging;

namespace Articles.Infrastructure.Persistence;

public sealed class ActivityLog : IActivityLog
{
    private readonly AppDbContext _db;
    private readonly TimeProvider _time;
    private readonly ILogger<ActivityLog> _logger;

    public ActivityLog(AppDbContext db, ILogger<ActivityLog> logger, TimeProvider? time = null)
    {
        _db = db;
        _logger = logger;
        _time = time ?? TimeProvider.System;
    }

    public async Task RecordAsync(string actor, string action, string? subject = null, CancellationToken ct = default)
    {
        try
        {
            var now = _time.GetUtcNow();
            _db.ActivityEntries.Add(new ActivityEntry(now, actor, action, subject));
            await _db.SaveChangesAsync(ct);

            // Sign-ins happen a few times a day at most, which makes them a cheap moment to clear out old history.
            if (action == Activity.SignedIn)
            {
                var cutoff = now - Activity.Retention;
                await _db.ActivityEntries.Where(e => e.At < cutoff).ExecuteDeleteAsync(ct);
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // The action being described has already happened. Losing its history line is better than
            // telling the person it failed.
            _logger.LogError(ex, "Could not record activity '{Action}' by {Actor}", action, actor);

            // Drop the unsaved line so it is not written by a later save. If the context itself is the
            // problem, there is nothing to drop.
            try { _db.ChangeTracker.Clear(); } catch (ObjectDisposedException) { }
        }
    }

    public Task<int> CountSinceAsync(string action, DateTimeOffset since, CancellationToken ct = default) =>
        _db.ActivityEntries.CountAsync(e => e.Action == action && e.At >= since, ct);

    public async Task<PagedResult<ActivityDto>> GetPageAsync(int pageNumber, int pageSize, CancellationToken ct = default)
    {
        var query = _db.ActivityEntries.AsNoTracking();
        var total = await query.CountAsync(ct);
        var items = await query
            .OrderByDescending(e => e.At)
            .Skip((pageNumber - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(ct);

        return new PagedResult<ActivityDto>(items.Select(e => e.ToDto()).ToList(), pageNumber, pageSize, total);
    }
}

public class ActivityEntryConfiguration : IEntityTypeConfiguration<ActivityEntry>
{
    public void Configure(EntityTypeBuilder<ActivityEntry> builder)
    {
        builder.ToTable("activity");
        builder.HasKey(e => e.Id);

        builder.Property(e => e.Actor).IsRequired().HasMaxLength(ActivityEntry.MaxActorLength);
        builder.Property(e => e.Action).IsRequired().HasMaxLength(ActivityEntry.MaxActionLength);
        builder.Property(e => e.Subject).IsRequired().HasMaxLength(ActivityEntry.MaxSubjectLength);

        // Newest first, and the clear-out of old lines.
        builder.HasIndex(e => e.At);
    }
}
