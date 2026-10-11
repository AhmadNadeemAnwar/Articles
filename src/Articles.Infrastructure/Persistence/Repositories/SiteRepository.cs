using Articles.Domain.Entities;
using Articles.Domain.Enums;
using Articles.Domain.Interfaces;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Articles.Infrastructure.Persistence.Repositories;

public sealed class SiteRepository : ISiteRepository
{
    private readonly AppDbContext _db;

    public SiteRepository(AppDbContext db) => _db = db;

    public async Task<IReadOnlyList<(Category Category, int PublishedPosts)>> GetCategoriesAsync(CancellationToken ct = default)
    {
        var rows = await _db.Categories.AsNoTracking()
            .OrderBy(c => c.SortOrder).ThenBy(c => c.Name)
            .Select(c => new { Category = c, Posts = _db.Posts.Count(p => p.CategoryId == c.Id && p.Status == PostStatus.Published) })
            .ToListAsync(ct);

        return rows.Select(r => (r.Category, r.Posts)).ToList();
    }

    public Task<Category?> GetCategoryAsync(Guid id, CancellationToken ct = default) =>
        _db.Categories.FirstOrDefaultAsync(c => c.Id == id, ct);

    public Task<bool> CategorySlugExistsAsync(string slug, CancellationToken ct = default) =>
        _db.Categories.AnyAsync(c => c.Slug == slug, ct);

    public Task<bool> CategoryNameExistsAsync(string name, Guid? exceptId, CancellationToken ct = default)
    {
        var lowered = name.Trim().ToLower();
        return _db.Categories.AnyAsync(c => c.Name.ToLower() == lowered && (exceptId == null || c.Id != exceptId), ct);
    }

    public Task<int> CountCategoriesAsync(CancellationToken ct = default) => _db.Categories.CountAsync(ct);

    public async Task<int> NextCategorySortOrderAsync(CancellationToken ct = default) =>
        (await _db.Categories.MaxAsync(c => (int?)c.SortOrder, ct) ?? 0) + 1;

    public async Task AddCategoryAsync(Category category, CancellationToken ct = default) =>
        await _db.Categories.AddAsync(category, ct);

    public void RemoveCategory(Category category) => _db.Categories.Remove(category);

    public async Task<string?> GetSettingAsync(string key, CancellationToken ct = default) =>
        await _db.SiteSettings.AsNoTracking().Where(s => s.Key == key).Select(s => s.Value).FirstOrDefaultAsync(ct);

    public async Task SetSettingAsync(string key, string value, CancellationToken ct = default)
    {
        var setting = await _db.SiteSettings.FirstOrDefaultAsync(s => s.Key == key, ct);
        if (setting is null) await _db.SiteSettings.AddAsync(new SiteSetting(key, value), ct);
        else setting.Set(value);
    }
}

public class CategoryConfiguration : IEntityTypeConfiguration<Category>
{
    public void Configure(EntityTypeBuilder<Category> builder)
    {
        builder.ToTable("categories");
        builder.HasKey(c => c.Id);

        builder.Property(c => c.Name).IsRequired().HasMaxLength(Category.MaxNameLength);
        builder.Property(c => c.Slug).IsRequired().HasMaxLength(60);

        builder.HasIndex(c => c.Slug).IsUnique();
    }
}

public class SiteSettingConfiguration : IEntityTypeConfiguration<SiteSetting>
{
    public void Configure(EntityTypeBuilder<SiteSetting> builder)
    {
        builder.ToTable("site_settings");
        builder.HasKey(s => s.Key);

        builder.Property(s => s.Key).HasMaxLength(SiteSetting.MaxKeyLength);
        builder.Property(s => s.Value).IsRequired().HasMaxLength(SiteSetting.MaxValueLength);
    }
}
