using Articles.Domain.Common;
using Articles.Domain.Exceptions;

namespace Articles.Domain.Entities;

/// <summary>
/// One of a handful of broad shelves the owner sorts posts onto ("Life and lessons", "Technology and
/// AI"). A post sits on at most one. Topics (tags) are separate: many per post, and made up freely.
/// </summary>
public class Category : BaseEntity
{
    public const int MaxNameLength = 40;

    public string Name { get; private set; } = null!;

    /// <summary>Set once from the first name and never changed, so links to a category keep working after a rename.</summary>
    public string Slug { get; private set; } = null!;

    /// <summary>Position among the tabs on the home page; lower comes first.</summary>
    public int SortOrder { get; private set; }

    private Category() { }

    public Category(string name, string slug, int sortOrder)
    {
        if (string.IsNullOrWhiteSpace(slug)) throw new DomainException("A category name needs at least one letter or number.");

        Name = Clean(name);
        Slug = slug;
        SortOrder = sortOrder;
    }

    public void Rename(string name)
    {
        Name = Clean(name);
        Touch();
    }

    private static string Clean(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) throw new DomainException("Category name is required.");
        var trimmed = name.Trim();
        if (trimmed.Length > MaxNameLength) throw new DomainException($"Category name cannot exceed {MaxNameLength} characters.");
        return trimmed;
    }
}

/// <summary>A single named setting for the whole site, such as which theme readers see.</summary>
public class SiteSetting
{
    public const int MaxKeyLength = 60;

    /// <summary>Room for the longest setting, the note shown on every post (1,000 characters), with space to spare.</summary>
    public const int MaxValueLength = 2000;

    public string Key { get; private set; } = null!;
    public string Value { get; private set; } = null!;

    private SiteSetting() { }

    public SiteSetting(string key, string value)
    {
        Key = key;
        Value = Checked(value);
    }

    public void Set(string value) => Value = Checked(value);

    // Checked here as well as by the database, because the SQLite used in tests does not enforce column lengths and
    // Postgres in production does: a value too long for the column must fail the same way everywhere.
    private static string Checked(string value) =>
        value.Length <= MaxValueLength ? value : throw new DomainException($"A site setting can be at most {MaxValueLength} characters.");
}
