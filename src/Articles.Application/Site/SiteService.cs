using FluentValidation;
using Articles.Application.Admin;
using Articles.Application.Common;
using Articles.Domain.Entities;
using Articles.Domain.Exceptions;
using Articles.Domain.Interfaces;

namespace Articles.Application.Site;

public sealed record CategoryDto(Guid Id, string Name, string Slug);

public sealed record CategoryWithCountDto(Guid Id, string Name, string Slug, int PostCount);

/// <summary>What the public site needs before it draws anything: how it should look and how posts are shelved.</summary>
/// <param name="SubscribeEnabled">Whether the site can send email yet; the subscribe form is hidden until it can.</param>
/// <param name="Colors">The owner's own three colours. Present only when the theme is "custom".</param>
public sealed record SiteDto(string Theme, IReadOnlyList<CategoryWithCountDto> Categories, bool SubscribeEnabled = false, ThemeColorsDto? Colors = null);

/// <summary>The three colours a custom theme is made from. Every other colour on the site is worked out from these.</summary>
/// <param name="Main">The header, as #rrggbb.</param>
/// <param name="Background">The page behind the text, as #rrggbb.</param>
/// <param name="Accent">Links and buttons, as #rrggbb. The same as <paramref name="Main"/> for colours saved before there were three.</param>
public sealed record ThemeColorsDto(string Main, string Background, string Accent);

/// <param name="Colors">The custom colours last saved, or a starting suggestion, so the pickers always have something to show.</param>
public sealed record SiteSettingsDto(string Theme, IReadOnlyList<string> AvailableThemes, ThemeColorsDto Colors);

/// <param name="Main">Needed only when <paramref name="Theme"/> is "custom".</param>
/// <param name="Background">Needed only when <paramref name="Theme"/> is "custom".</param>
/// <param name="Accent">Optional; links and buttons take the main colour when it is left out.</param>
public sealed record UpdateSiteSettingsRequest(string Theme, string? Main = null, string? Background = null, string? Accent = null);

public sealed record SaveCategoryRequest(string Name);

public sealed class SaveCategoryRequestValidator : AbstractValidator<SaveCategoryRequest>
{
    public SaveCategoryRequestValidator() =>
        RuleFor(x => x.Name).NotEmpty().WithMessage("Category name is required.").MaximumLength(Category.MaxNameLength);
}

public interface ISiteService
{
    Task<SiteDto> GetPublicAsync(CancellationToken ct = default);

    Task<SiteSettingsDto> GetSettingsAsync(CancellationToken ct = default);
    Task<SiteSettingsDto> UpdateSettingsAsync(UpdateSiteSettingsRequest request, string admin, CancellationToken ct = default);

    Task<IReadOnlyList<CategoryWithCountDto>> GetCategoriesAsync(CancellationToken ct = default);
    Task<CategoryWithCountDto> CreateCategoryAsync(SaveCategoryRequest request, string admin, CancellationToken ct = default);
    Task<CategoryWithCountDto> RenameCategoryAsync(Guid id, SaveCategoryRequest request, string admin, CancellationToken ct = default);
    Task DeleteCategoryAsync(Guid id, string admin, CancellationToken ct = default);
}

/// <summary>The looks the public site can wear. The first is what a site that has never chosen gets.</summary>
public static class Themes
{
    public const string SettingKey = "theme";
    public const string Blue = "blue";
    public const string SeaGreen = "seagreen";

    /// <summary>The owner's own colours, kept in <see cref="ThemeColors.SettingKey"/>.</summary>
    public const string Custom = "custom";

    public static readonly IReadOnlyList<string> All = [Blue, SeaGreen, Custom];

    public static string Normalise(string? value)
    {
        var wanted = value?.Trim().ToLowerInvariant();
        return All.FirstOrDefault(theme => theme == wanted)
            ?? throw new DomainException($"Theme must be one of: {string.Join(", ", All)}.");
    }

    /// <summary>A stored value this version does not know (left by a newer or older one) falls back rather than breaking the site.</summary>
    public static string OrDefault(string? stored) => All.Contains(stored ?? string.Empty) ? stored! : Blue;
}

/// <summary>
/// The colours of a custom theme. They are only ever stored and sent as plain #rrggbb, so nothing
/// else can ride along into a reader's page, and a background is refused when no text could be read on it.
/// </summary>
public static partial class ThemeColors
{
    public const string SettingKey = "theme.colors";

    /// <summary>Body text must stand out from the page by at least this much (WCAG AAA for long reading).</summary>
    public const double MinimumTextContrast = 7;

    /// <summary>What the pickers start from before the owner has saved colours of their own.</summary>
    public static readonly ThemeColorsDto Suggested = new("#17694a", "#ffffff", "#17694a");

    // The two body-text colours the public site chooses between; the same pair is in its palette code.
    private const string DarkText = "#1c1e21";
    private const string LightText = "#ececee";

    [System.Text.RegularExpressions.GeneratedRegex("^#[0-9a-f]{6}$")]
    private static partial System.Text.RegularExpressions.Regex Hex();

    public static ThemeColorsDto Normalise(string? main, string? background, string? accent = null)
    {
        var header = Clean(main, "header colour");
        var colors = new ThemeColorsDto(header, Clean(background, "background colour"),
            string.IsNullOrWhiteSpace(accent) ? header : Clean(accent, "links and buttons colour"));

        if (BestTextContrast(colors.Background) < MinimumTextContrast)
            throw new DomainException("Text would be hard to read on that background. Choose a lighter or a darker one.");

        return colors;
    }

    /// <summary>Reads what <see cref="Store"/> wrote. Anything else (missing, damaged, from another version) is null.</summary>
    public static ThemeColorsDto? Read(string? stored)
    {
        var parts = (stored ?? string.Empty).Split(',');
        // Two parts were stored before links and buttons had a colour of their own; they took the main colour.
        if (parts.Length is not (2 or 3) || parts.Any(part => !Hex().IsMatch(part))) return null;
        return BestTextContrast(parts[1]) < MinimumTextContrast ? null : new ThemeColorsDto(parts[0], parts[1], parts.Length == 3 ? parts[2] : parts[0]);
    }

    public static string Store(ThemeColorsDto colors) => $"{colors.Main},{colors.Background},{colors.Accent}";

    private static string Clean(string? value, string name)
    {
        var clean = value?.Trim().ToLowerInvariant() ?? string.Empty;
        return Hex().IsMatch(clean) ? clean : throw new DomainException($"The {name} must be a colour like #17694a.");
    }

    /// <summary>The contrast of the better of dark and light text against a background.</summary>
    public static double BestTextContrast(string background) =>
        Math.Max(Contrast(background, DarkText), Contrast(background, LightText));

    /// <summary>The WCAG contrast ratio of two #rrggbb colours, from 1 (identical) to 21 (black on white).</summary>
    public static double Contrast(string first, string second)
    {
        var (a, b) = (Luminance(first), Luminance(second));
        return (Math.Max(a, b) + 0.05) / (Math.Min(a, b) + 0.05);
    }

    private static double Luminance(string hex)
    {
        static double Channel(string hex, int at)
        {
            var value = Convert.ToInt32(hex.Substring(at, 2), 16) / 255.0;
            return value <= 0.04045 ? value / 12.92 : Math.Pow((value + 0.055) / 1.055, 2.4);
        }

        return 0.2126 * Channel(hex, 1) + 0.7152 * Channel(hex, 3) + 0.0722 * Channel(hex, 5);
    }
}

public sealed class SiteService : ISiteService
{
    /// <summary>Categories are tabs across the home page; past a dozen they stop being a way to browse.</summary>
    public const int MaxCategories = 12;

    /// <summary>
    /// First parts of a public address that already belong to a page of the site. A category's address is the first
    /// part of its posts' addresses (/life/a-post/k3x9p), so a category may not take one. Keep in step with the site's list.
    /// </summary>
    public static readonly IReadOnlyList<string> ReservedAddresses =
        ["read", "p", "media", "assets", "tag", "search", "privacy", "subscribe", "unsubscribe", "api", "rss", "sitemap", "latest", "admin"];

    private readonly ISiteRepository _site;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IActivityLog _activity;

    public SiteService(ISiteRepository site, IUnitOfWork unitOfWork, IActivityLog? activity = null)
    {
        _site = site;
        _unitOfWork = unitOfWork;
        _activity = activity ?? NoActivityLog.Instance;
    }

    public async Task<SiteDto> GetPublicAsync(CancellationToken ct = default)
    {
        var (theme, colors) = await CurrentLookAsync(ct);
        return new SiteDto(theme, await GetCategoriesAsync(ct), Colors: theme == Themes.Custom ? colors : null);
    }

    public async Task<SiteSettingsDto> GetSettingsAsync(CancellationToken ct = default)
    {
        var (theme, colors) = await CurrentLookAsync(ct);
        return new SiteSettingsDto(theme, Themes.All, colors ?? ThemeColors.Suggested);
    }

    /// <summary>The theme in use and the custom colours on file. A custom theme whose colours cannot be read is the default theme.</summary>
    private async Task<(string Theme, ThemeColorsDto? Colors)> CurrentLookAsync(CancellationToken ct)
    {
        var theme = Themes.OrDefault(await _site.GetSettingAsync(Themes.SettingKey, ct));
        var colors = ThemeColors.Read(await _site.GetSettingAsync(ThemeColors.SettingKey, ct));
        return (theme == Themes.Custom && colors is null ? Themes.Blue : theme, colors);
    }

    public async Task<SiteSettingsDto> UpdateSettingsAsync(UpdateSiteSettingsRequest request, string admin, CancellationToken ct = default)
    {
        var theme = Themes.Normalise(request.Theme);
        var (current, stored) = await CurrentLookAsync(ct);

        // Colours matter only to the custom theme; choosing a ready-made one leaves the saved colours for next time.
        var colors = theme == Themes.Custom ? ThemeColors.Normalise(request.Main, request.Background, request.Accent) : stored;

        if (theme != current || colors != stored)
        {
            await _site.SetSettingAsync(Themes.SettingKey, theme, ct);
            if (colors is not null && colors != stored) await _site.SetSettingAsync(ThemeColors.SettingKey, ThemeColors.Store(colors), ct);
            await _unitOfWork.SaveChangesAsync(ct);
            await _activity.RecordAsync(admin, Activity.ChangedTheme, $"{Describe(current, stored)} to {Describe(theme, colors)}", ct);
        }

        return new SiteSettingsDto(theme, Themes.All, colors ?? ThemeColors.Suggested);
    }

    private static string Describe(string theme, ThemeColorsDto? colors) =>
        theme == Themes.Custom && colors is not null ? $"custom (header {colors.Main}, links {colors.Accent}, page {colors.Background})" : theme;

    public async Task<IReadOnlyList<CategoryWithCountDto>> GetCategoriesAsync(CancellationToken ct = default) =>
        (await _site.GetCategoriesAsync(ct)).Select(row => ToDto(row.Category, row.PublishedPosts)).ToList();

    public async Task<CategoryWithCountDto> CreateCategoryAsync(SaveCategoryRequest request, string admin, CancellationToken ct = default)
    {
        if (await _site.CountCategoriesAsync(ct) >= MaxCategories)
            throw new DomainException($"There can be at most {MaxCategories} categories. Remove one before adding another.");

        var name = request.Name.Trim();
        if (await _site.CategoryNameExistsAsync(name, null, ct)) throw new ConflictException($"There is already a category called \"{name}\".");

        var slug = SlugGenerator.Generate(name);
        if (slug.Length == 0) throw new DomainException("A category name needs at least one letter or number.");
        if (ReservedAddresses.Contains(slug)) throw new DomainException($"\"{name}\" is already the address of a page on the site. Choose another name.");
        // Two names can reduce to the same address ("AI & ML" and "AI ML"); the second gets a short code.
        if (await _site.CategorySlugExistsAsync(slug, ct)) slug = SlugGenerator.WithCode(slug);

        var category = new Category(name, slug, await _site.NextCategorySortOrderAsync(ct));
        await _site.AddCategoryAsync(category, ct);
        await _unitOfWork.SaveChangesAsync(ct);

        await _activity.RecordAsync(admin, Activity.AddedCategory, category.Name, ct);
        return ToDto(category, 0);
    }

    public async Task<CategoryWithCountDto> RenameCategoryAsync(Guid id, SaveCategoryRequest request, string admin, CancellationToken ct = default)
    {
        var category = await _site.GetCategoryAsync(id, ct) ?? throw new NotFoundException(nameof(Category), id);
        var name = request.Name.Trim();
        if (await _site.CategoryNameExistsAsync(name, id, ct)) throw new ConflictException($"There is already a category called \"{name}\".");

        var previous = category.Name;
        category.Rename(name);
        await _unitOfWork.SaveChangesAsync(ct);

        if (previous != category.Name) await _activity.RecordAsync(admin, Activity.RenamedCategory, $"{previous} to {category.Name}", ct);

        var count = (await _site.GetCategoriesAsync(ct)).FirstOrDefault(row => row.Category.Id == id).PublishedPosts;
        return ToDto(category, count);
    }

    public async Task DeleteCategoryAsync(Guid id, string admin, CancellationToken ct = default)
    {
        var category = await _site.GetCategoryAsync(id, ct) ?? throw new NotFoundException(nameof(Category), id);

        // Posts on this shelf are kept; they simply stop having a category.
        _site.RemoveCategory(category);
        await _unitOfWork.SaveChangesAsync(ct);

        await _activity.RecordAsync(admin, Activity.DeletedCategory, category.Name, ct);
    }

    private static CategoryWithCountDto ToDto(Category category, int posts) => new(category.Id, category.Name, category.Slug, posts);
}
