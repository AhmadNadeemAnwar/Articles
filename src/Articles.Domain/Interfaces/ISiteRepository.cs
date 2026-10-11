using Articles.Domain.Entities;

namespace Articles.Domain.Interfaces;

/// <summary>Categories and site-wide settings: the small amount of state that shapes the whole public site.</summary>
public interface ISiteRepository
{
    /// <summary>Every category in tab order, each with its number of published posts.</summary>
    Task<IReadOnlyList<(Category Category, int PublishedPosts)>> GetCategoriesAsync(CancellationToken ct = default);
    Task<Category?> GetCategoryAsync(Guid id, CancellationToken ct = default);
    Task<bool> CategorySlugExistsAsync(string slug, CancellationToken ct = default);
    Task<bool> CategoryNameExistsAsync(string name, Guid? exceptId, CancellationToken ct = default);
    Task<int> CountCategoriesAsync(CancellationToken ct = default);
    Task<int> NextCategorySortOrderAsync(CancellationToken ct = default);
    Task AddCategoryAsync(Category category, CancellationToken ct = default);
    void RemoveCategory(Category category);

    Task<string?> GetSettingAsync(string key, CancellationToken ct = default);

    /// <summary>Creates or replaces the setting. Staged, not saved.</summary>
    Task SetSettingAsync(string key, string value, CancellationToken ct = default);
}
