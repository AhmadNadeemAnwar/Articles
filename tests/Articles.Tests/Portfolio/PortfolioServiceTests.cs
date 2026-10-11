using FluentAssertions;
using Articles.Application.Portfolio;
using Articles.Domain.Exceptions;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Articles.Tests.Portfolio;

/// <summary>An in-memory stand-in for the GitHub repository that enforces the same sha rules GitHub does.</summary>
public sealed class FakePortfolioStore : IPortfolioContentStore
{
    private readonly Dictionary<string, (string Content, string Sha)> _files = new(StringComparer.Ordinal);
    private int _counter;

    public bool IsConfigured { get; set; } = true;
    public string? Repo => IsConfigured ? "owner/portfolio" : null;
    public string? Branch => IsConfigured ? "main" : null;
    public string ContentRoot => "src/content";
    public List<string> Commits { get; } = new();

    public IReadOnlyDictionary<string, string> Files => _files.ToDictionary(kv => kv.Key, kv => kv.Value.Content);

    public string Seed(string path, string content)
    {
        var sha = $"sha{++_counter}";
        _files[path] = (content, sha);
        return sha;
    }

    public Task<IReadOnlyList<RepoFile>> ListAsync(string directory, CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<RepoFile>>(_files
            .Where(kv => Path.GetDirectoryName(kv.Key)!.Replace('\\', '/') == directory)
            .Select(kv => new RepoFile(Path.GetFileName(kv.Key), kv.Key, kv.Value.Sha, null))
            .ToList());

    public Task<RepoFile?> GetAsync(string path, CancellationToken ct = default) =>
        Task.FromResult(_files.TryGetValue(path, out var file) ? new RepoFile(Path.GetFileName(path), path, file.Sha, file.Content) : null);

    public Task<string> PutAsync(string path, string content, string? sha, string message, CancellationToken ct = default)
    {
        var exists = _files.TryGetValue(path, out var current);
        if (exists && sha is null) throw new ConflictException("exists");
        if (exists && sha != current.Sha) throw new ConflictException("stale");
        if (!exists && sha is not null) throw new ConflictException("missing");

        Commits.Add(message);
        return Task.FromResult(Seed(path, content));
    }

    public Task DeleteAsync(string path, string sha, string message, CancellationToken ct = default)
    {
        if (!_files.TryGetValue(path, out var current)) throw new NotFoundException(path);
        if (sha != current.Sha) throw new ConflictException("stale");

        Commits.Add(message);
        _files.Remove(path);
        return Task.CompletedTask;
    }
}

public class PortfolioServiceTests
{
    private readonly FakePortfolioStore _store = new();
    private readonly PortfolioService _service;

    public PortfolioServiceTests() => _service = new PortfolioService(_store, NullLogger<PortfolioService>.Instance);

    private const string BlogMarkdown = "---\ntitle: Hello\ndescription: First post\ndate: 2026-09-20\ntags: [meta]\n---\n\nBody text\n";

    private static SavePortfolioEntryRequest Save(string? sha, string title = "New post", string body = "Body") => new(sha, new Dictionary<string, object?>
    {
        ["title"] = title, ["description"] = "A description", ["date"] = "2026-10-01"
    }, body);

    [Fact]
    public async Task Listing_reads_each_file_and_sorts_newest_first()
    {
        _store.Seed("src/content/blog/old.md", "---\ntitle: Old one\ndescription: d\ndate: 2025-01-01\n---\n\nx\n");
        _store.Seed("src/content/blog/new.md", "---\ntitle: New one\ndescription: d\ndate: 2026-06-01\ndraft: true\n---\n\nx\n");
        _store.Seed("src/content/projects/other.md", "---\ntitle: Elsewhere\nsummary: s\ndate: 2026-01-01\n---\n\nx\n");

        var list = await _service.ListAsync("blog");

        list.Select(e => e.Title).Should().Equal("New one", "Old one");
        list[0].Draft.Should().BeTrue();
        list[0].Slug.Should().Be("new");
        list[0].Date.Should().Be("2026-06-01");
    }

    [Fact]
    public async Task Listing_ignores_files_the_site_would_not_load()
    {
        _store.Seed("src/content/updates/ok.md", "---\ntitle: Fine\ndate: 2026-01-01\n---\n");
        _store.Seed("src/content/updates/notes.txt", "not content");
        _store.Seed("src/content/updates/page.mdx", "---\ntitle: Mdx\ndate: 2026-01-01\n---\n");

        (await _service.ListAsync("updates")).Select(e => e.Slug).Should().Equal("ok");
    }

    [Fact]
    public async Task One_broken_file_is_flagged_without_hiding_the_others()
    {
        _store.Seed("src/content/blog/good.md", BlogMarkdown);
        _store.Seed("src/content/blog/broken.md", "this has no front matter");

        var list = await _service.ListAsync("blog");

        list.Should().HaveCount(2);
        list.Single(e => e.Slug == "broken").Problem.Should().Contain("front matter");
        list.Single(e => e.Slug == "good").Problem.Should().BeNull();
    }

    [Fact]
    public async Task Opening_an_entry_returns_its_fields_body_and_version()
    {
        var sha = _store.Seed("src/content/blog/hello.md", BlogMarkdown);

        var entry = await _service.GetAsync("blog", "hello");

        entry.Sha.Should().Be(sha);
        entry.Frontmatter["title"].Should().Be("Hello");
        entry.Body.Should().Be("Body text\n");
    }

    [Fact]
    public async Task A_blog_entry_stored_as_mdx_is_found_too()
    {
        _store.Seed("src/content/blog/fancy.mdx", BlogMarkdown);

        (await _service.GetAsync("blog", "fancy")).Slug.Should().Be("fancy");
    }

    [Fact]
    public async Task Opening_something_that_does_not_exist_is_not_found()
    {
        (await Record.ExceptionAsync(() => _service.GetAsync("blog", "missing"))).Should().BeOfType<NotFoundException>();
    }

    [Fact]
    public async Task Creating_an_entry_commits_a_new_file_with_a_clear_message()
    {
        var saved = await _service.SaveAsync("blog", "new-post", Save(null), "owner@example.com");

        _store.Files["src/content/blog/new-post.md"].Should().StartWith("---\ntitle: New post\n");
        _store.Commits.Should().ContainSingle().Which.Should().Be("Add blog/new-post (admin portal)");
        saved.Sha.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task Creating_over_an_existing_entry_is_refused_rather_than_overwriting_it()
    {
        _store.Seed("src/content/blog/taken.md", BlogMarkdown);

        var act = () => _service.SaveAsync("blog", "taken", Save(null), "owner@example.com");

        await act.Should().ThrowAsync<ConflictException>().WithMessage("*already exists*");
        _store.Files["src/content/blog/taken.md"].Should().Be(BlogMarkdown);
    }

    [Fact]
    public async Task Updating_with_the_current_version_commits_the_change()
    {
        var sha = _store.Seed("src/content/blog/hello.md", BlogMarkdown);

        var saved = await _service.SaveAsync("blog", "hello", Save(sha, title: "Hello again"), "owner@example.com");

        _store.Files["src/content/blog/hello.md"].Should().Contain("title: Hello again");
        _store.Commits.Should().ContainSingle().Which.Should().Be("Update blog/hello (admin portal)");
        saved.Sha.Should().NotBe(sha);
    }

    [Fact]
    public async Task Updating_an_mdx_entry_keeps_its_extension()
    {
        var sha = _store.Seed("src/content/blog/fancy.mdx", BlogMarkdown);

        await _service.SaveAsync("blog", "fancy", Save(sha), "owner@example.com");

        _store.Files.Keys.Should().Contain("src/content/blog/fancy.mdx").And.NotContain("src/content/blog/fancy.md");
    }

    [Fact]
    public async Task Saving_over_a_version_someone_else_changed_is_refused_and_nothing_is_written()
    {
        var staleSha = _store.Seed("src/content/blog/hello.md", BlogMarkdown);
        _store.Seed("src/content/blog/hello.md", BlogMarkdown.Replace("Hello", "Changed on GitHub"));

        var act = () => _service.SaveAsync("blog", "hello", Save(staleSha, title: "My edit"), "owner@example.com");

        await act.Should().ThrowAsync<ConflictException>().WithMessage("*changed somewhere else*");
        _store.Files["src/content/blog/hello.md"].Should().Contain("Changed on GitHub");
        _store.Commits.Should().BeEmpty();
    }

    [Fact]
    public async Task Updating_an_entry_that_has_vanished_is_not_found()
    {
        var act = () => _service.SaveAsync("blog", "gone", Save("sha1"), "owner@example.com");

        await act.Should().ThrowAsync<NotFoundException>();
    }

    [Fact]
    public async Task An_invalid_entry_is_rejected_before_anything_reaches_the_repository()
    {
        var bad = new SavePortfolioEntryRequest(null, new Dictionary<string, object?> { ["title"] = "No date or description" }, "Body");

        var act = () => _service.SaveAsync("blog", "bad", bad, "owner@example.com");

        await act.Should().ThrowAsync<DomainException>().WithMessage("*date is required*");
        _store.Files.Should().BeEmpty();
        _store.Commits.Should().BeEmpty();
    }

    [Theory]
    [InlineData("../../etc/passwd")]
    [InlineData("Has Capitals")]
    [InlineData("a/b")]
    [InlineData("..")]
    [InlineData("name.md")]
    public async Task Unsafe_file_names_are_refused(string slug)
    {
        (await Record.ExceptionAsync(() => _service.SaveAsync("blog", slug, Save(null), "x"))).Should().BeOfType<DomainException>();
        (await Record.ExceptionAsync(() => _service.GetAsync("blog", slug))).Should().BeOfType<DomainException>();
        (await Record.ExceptionAsync(() => _service.DeleteAsync("blog", slug, "sha", "x"))).Should().BeOfType<DomainException>();
        _store.Files.Should().BeEmpty();
    }

    [Fact]
    public async Task Only_the_three_known_collections_can_be_touched()
    {
        foreach (var collection in new[] { "secrets", "..", "", "BLOG", "src" })
        {
            (await Record.ExceptionAsync(() => _service.ListAsync(collection))).Should().BeOfType<NotFoundException>(collection);
            (await Record.ExceptionAsync(() => _service.SaveAsync(collection, "x", Save(null), "x"))).Should().BeOfType<NotFoundException>(collection);
        }
    }

    [Fact]
    public async Task Deleting_with_the_current_version_removes_the_file_and_commits_it()
    {
        var sha = _store.Seed("src/content/blog/hello.md", BlogMarkdown);

        await _service.DeleteAsync("blog", "hello", sha, "owner@example.com");

        _store.Files.Should().BeEmpty();
        _store.Commits.Should().ContainSingle().Which.Should().Be("Delete blog/hello (admin portal)");
    }

    [Fact]
    public async Task Deleting_a_version_that_has_changed_is_refused()
    {
        _store.Seed("src/content/blog/hello.md", BlogMarkdown);

        var act = () => _service.DeleteAsync("blog", "hello", "an-old-sha", "owner@example.com");

        await act.Should().ThrowAsync<ConflictException>();
        _store.Files.Should().ContainKey("src/content/blog/hello.md");
    }

    [Fact]
    public async Task Everything_refuses_politely_while_github_is_not_configured()
    {
        _store.IsConfigured = false;

        (await Record.ExceptionAsync(() => _service.ListAsync("blog"))).Should().BeOfType<ServiceUnavailableException>();
        (await Record.ExceptionAsync(() => _service.GetAsync("blog", "x"))).Should().BeOfType<ServiceUnavailableException>();
        (await Record.ExceptionAsync(() => _service.SaveAsync("blog", "x", Save(null), "x"))).Should().BeOfType<ServiceUnavailableException>();
        _service.GetStatus().Configured.Should().BeFalse();
    }

    [Fact]
    public void Status_reports_the_repository_when_configured()
    {
        var status = _service.GetStatus();

        status.Configured.Should().BeTrue();
        status.Repo.Should().Be("owner/portfolio");
        status.Branch.Should().Be("main");
    }
}
