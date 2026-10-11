using Articles.Domain.Common;
using Articles.Domain.Entities;
using Articles.Domain.Interfaces;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Articles.Infrastructure.Persistence.Repositories;

public sealed class SubscriberRepository : ISubscriberRepository
{
    private readonly AppDbContext _db;

    public SubscriberRepository(AppDbContext db) => _db = db;

    public Task<Subscriber?> GetByEmailAsync(string email, CancellationToken ct = default) =>
        _db.Subscribers.FirstOrDefaultAsync(s => s.Email == email, ct);

    public Task<Subscriber?> GetAsync(Guid id, CancellationToken ct = default) =>
        _db.Subscribers.FirstOrDefaultAsync(s => s.Id == id, ct);

    public async Task AddAsync(Subscriber subscriber, CancellationToken ct = default) =>
        await _db.Subscribers.AddAsync(subscriber, ct);

    public void Remove(Subscriber subscriber) => _db.Subscribers.Remove(subscriber);

    private IQueryable<Subscriber> Unnotified(Guid postId) =>
        _db.Subscribers.Where(s => s.Status == SubscriberStatus.Confirmed && (s.LastNotifiedPostId == null || s.LastNotifiedPostId != postId));

    public async Task<IReadOnlyList<Subscriber>> GetUnnotifiedAsync(Guid postId, int limit, CancellationToken ct = default) =>
        await Unnotified(postId).OrderBy(s => s.CreatedAt).Take(limit).ToListAsync(ct);

    public Task<int> CountUnnotifiedAsync(Guid postId, CancellationToken ct = default) => Unnotified(postId).CountAsync(ct);

    public Task<int> CountAsync(SubscriberStatus status, CancellationToken ct = default) =>
        _db.Subscribers.CountAsync(s => s.Status == status, ct);

    public async Task<PagedResult<Subscriber>> GetPageAsync(int pageNumber, int pageSize, CancellationToken ct = default)
    {
        var query = _db.Subscribers.AsNoTracking();
        var total = await query.CountAsync(ct);
        var items = await query.OrderByDescending(s => s.CreatedAt).Skip((pageNumber - 1) * pageSize).Take(pageSize).ToListAsync(ct);
        return new PagedResult<Subscriber>(items, pageNumber, pageSize, total);
    }
}

public class SubscriberConfiguration : IEntityTypeConfiguration<Subscriber>
{
    public void Configure(EntityTypeBuilder<Subscriber> builder)
    {
        builder.ToTable("subscribers");
        builder.HasKey(s => s.Id);

        builder.Property(s => s.Email).IsRequired().HasMaxLength(Subscriber.MaxEmailLength);
        builder.Property(s => s.Status).HasConversion<int>();

        // One row per address, however many times it is entered.
        builder.HasIndex(s => s.Email).IsUnique();
        builder.HasIndex(s => s.Status);
    }
}
