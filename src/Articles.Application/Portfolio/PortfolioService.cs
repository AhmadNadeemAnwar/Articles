using Articles.Application.Admin;
using Articles.Domain.Exceptions;
using Microsoft.Extensions.Logging;

namespace Articles.Application.Portfolio;

public sealed record PortfolioStatusDto(bool Configured, string? Repo, string? Branch);

public sealed record PortfolioEntrySummaryDto(string Slug, string FileName, string Title, string? Date, bool Draft, string Sha, string? Problem);

public sealed record PortfolioEntryDto(string Collection, string Slug, string Sha, Dictionary<string, object?> Frontmatter, string Body);

/// <param name="Sha">The version being edited; omit it to create a new entry. A mismatch means someone else changed the file.</param>
public sealed record SavePortfolioEntryRequest(string? Sha, Dictionary<string, object?> Frontmatter, string Body);

public sealed record RepoFile(string Name, string Path, string Sha, string? Content);

/// <summary>Reads and writes files in the portfolio's git repository.</summary>
public interface IPortfolioContentStore
{
    bool IsConfigured { get; }
    string? Repo { get; }
    string? Branch { get; }

    /// <summary>The directory that holds the collections, e.g. "src/content".</summary>
    string ContentRoot { get; }

    Task<IReadOnlyList<RepoFile>> ListAsync(string directory, CancellationToken ct = default);
    Task<RepoFile?> GetAsync(string path, CancellationToken ct = default);

    /// <returns>The new file's sha.</returns>
    Task<string> PutAsync(string path, string content, string? sha, string message, CancellationToken ct = default);
    Task DeleteAsync(string path, string sha, string message, CancellationToken ct = default);
}

public interface IPortfolioService
{
    PortfolioStatusDto GetStatus();
    Task<IReadOnlyList<PortfolioEntrySummaryDto>> ListAsync(string collection, CancellationToken ct = default);
    Task<PortfolioEntryDto> GetAsync(string collection, string slug, CancellationToken ct = default);
    Task<PortfolioEntryDto> SaveAsync(string collection, string slug, SavePortfolioEntryRequest request, string admin, CancellationToken ct = default);
    Task DeleteAsync(string collection, string slug, string sha, string admin, CancellationToken ct = default);
}

public sealed class PortfolioService : IPortfolioService
{
    private const int ParallelFetches = 6;

    private readonly IPortfolioContentStore _store;
    private readonly ILogger<PortfolioService> _logger;
    private readonly IActivityLog _activity;

    public PortfolioService(IPortfolioContentStore store, ILogger<PortfolioService> logger, IActivityLog? activity = null)
    {
        _store = store;
        _logger = logger;
        _activity = activity ?? NoActivityLog.Instance;
    }

    public PortfolioStatusDto GetStatus() => new(_store.IsConfigured, _store.Repo, _store.Branch);

    public async Task<IReadOnlyList<PortfolioEntrySummaryDto>> ListAsync(string collection, CancellationToken ct = default)
    {
        EnsureUsable(collection);

        var files = (await _store.ListAsync(Directory(collection), ct))
            .Where(f => PortfolioContent.ExtensionsFor(collection).Contains(Path.GetExtension(f.Name), StringComparer.OrdinalIgnoreCase))
            .ToList();

        // The listing has no titles, so each file is read. A few at a time keeps this quick without hammering GitHub.
        using var gate = new SemaphoreSlim(ParallelFetches);
        var summaries = await Task.WhenAll(files.Select(async file =>
        {
            await gate.WaitAsync(ct);
            try { return await SummariseAsync(file, ct); }
            finally { gate.Release(); }
        }));

        return summaries
            .OrderByDescending(s => s.Date ?? string.Empty, StringComparer.Ordinal)
            .ThenBy(s => s.Slug, StringComparer.Ordinal)
            .ToList();
    }

    public async Task<PortfolioEntryDto> GetAsync(string collection, string slug, CancellationToken ct = default)
    {
        EnsureUsable(collection);
        EnsureSlug(slug);

        var file = await FindAsync(collection, slug, ct) ?? throw new NotFoundException("Entry", $"{collection}/{slug}");
        var (frontmatter, body) = PortfolioContent.Parse(file.Content ?? string.Empty);
        return new PortfolioEntryDto(collection, slug, file.Sha, frontmatter, body);
    }

    public async Task<PortfolioEntryDto> SaveAsync(string collection, string slug, SavePortfolioEntryRequest request, string admin, CancellationToken ct = default)
    {
        EnsureUsable(collection);
        EnsureSlug(slug);

        // Validated and rendered before anything is sent, so a bad edit can never reach the repository.
        var markdown = PortfolioContent.Compose(collection, request.Frontmatter, request.Body);

        var existing = await FindAsync(collection, slug, ct);
        string path;

        if (request.Sha is null)
        {
            if (existing is not null) throw new ConflictException($"'{slug}' already exists. Open it to edit instead.");
            path = $"{Directory(collection)}/{slug}.md";
        }
        else
        {
            if (existing is null) throw new NotFoundException("Entry", $"{collection}/{slug}");
            if (!string.Equals(existing.Sha, request.Sha, StringComparison.Ordinal))
                throw new ConflictException("This entry was changed somewhere else since you opened it. Reload it and reapply your edit.");
            path = existing.Path;
        }

        var verb = request.Sha is null ? "Add" : "Update";
        var newSha = await _store.PutAsync(path, markdown, request.Sha, $"{verb} {collection}/{slug} (admin portal)", ct);

        _logger.LogInformation("Admin {Admin} saved portfolio entry {Collection}/{Slug}", admin, collection, slug);
        await _activity.RecordAsync(admin, Activity.SavedPortfolioEntry, $"{collection}/{slug}", ct);
        var (frontmatter, body) = PortfolioContent.Parse(markdown);
        return new PortfolioEntryDto(collection, slug, newSha, frontmatter, body);
    }

    public async Task DeleteAsync(string collection, string slug, string sha, string admin, CancellationToken ct = default)
    {
        EnsureUsable(collection);
        EnsureSlug(slug);

        var existing = await FindAsync(collection, slug, ct) ?? throw new NotFoundException("Entry", $"{collection}/{slug}");
        if (!string.Equals(existing.Sha, sha, StringComparison.Ordinal))
            throw new ConflictException("This entry was changed somewhere else since you opened it. Reload it before deleting.");

        await _store.DeleteAsync(existing.Path, existing.Sha, $"Delete {collection}/{slug} (admin portal)", ct);
        _logger.LogWarning("Admin {Admin} deleted portfolio entry {Collection}/{Slug}", admin, collection, slug);
        await _activity.RecordAsync(admin, Activity.DeletedPortfolioEntry, $"{collection}/{slug}", ct);
    }

    // ---- helpers -------------------------------------------------------------------------

    private string Directory(string collection) => $"{_store.ContentRoot.Trim('/')}/{collection}";

    private void EnsureUsable(string collection)
    {
        if (!PortfolioContent.IsCollection(collection)) throw new NotFoundException("Collection", collection);
        if (!_store.IsConfigured)
            throw new ServiceUnavailableException("Portfolio editing is not set up yet. Add the GitHub settings described in ADMIN.md.");
    }

    private static void EnsureSlug(string slug)
    {
        if (!PortfolioContent.IsValidSlug(slug))
            throw new DomainException("The file name may only use lowercase letters, numbers and hyphens.");
    }

    private async Task<RepoFile?> FindAsync(string collection, string slug, CancellationToken ct)
    {
        foreach (var extension in PortfolioContent.ExtensionsFor(collection))
        {
            var file = await _store.GetAsync($"{Directory(collection)}/{slug}{extension}", ct);
            if (file is not null) return file;
        }

        return null;
    }

    private async Task<PortfolioEntrySummaryDto> SummariseAsync(RepoFile file, CancellationToken ct)
    {
        var slug = Path.GetFileNameWithoutExtension(file.Name);

        try
        {
            var full = file.Content is null ? await _store.GetAsync(file.Path, ct) : file;
            var (frontmatter, _) = PortfolioContent.Parse(full?.Content ?? string.Empty);

            return new PortfolioEntrySummaryDto(
                slug, file.Name,
                frontmatter.GetValueOrDefault("title") as string ?? slug,
                frontmatter.GetValueOrDefault("date") as string,
                frontmatter.GetValueOrDefault("draft") is true,
                file.Sha, null);
        }
        catch (DomainException ex)
        {
            // One broken file must not hide the rest; surface it so it can be fixed.
            return new PortfolioEntrySummaryDto(slug, file.Name, slug, null, false, file.Sha, ex.Message);
        }
    }
}
