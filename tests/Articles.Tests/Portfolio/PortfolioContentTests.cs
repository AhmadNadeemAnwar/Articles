using System.Text.Json;
using FluentAssertions;
using Articles.Application.Portfolio;
using Articles.Domain.Exceptions;
using Xunit;

namespace Articles.Tests.Portfolio;

public class PortfolioContentTests
{
    // The four real files from the portfolio, verbatim. If the writer ever disagrees with how these
    // are formatted, the first edit to any of them would rewrite lines the author never touched.
    private const string ProjectFile = """
        ---
        title: Articles
        summary: A Medium-style publishing platform where anyone can write, publish, search and discuss posts.
        date: 2026-08-01
        tech: [ASP.NET Core, React, TipTap, SQL]
        featured: true
        ---

        Articles is a content publishing platform built with a layered ASP.NET Core Web API.

        ## What it does

        - Write and publish posts on any topic
        - Search and browse
        """;

    private const string BlogFile = """
        ---
        title: Hello, world — why I built this site
        description: A short note on why I'm writing in public, and how this site works.
        date: 2026-09-20
        tags: [meta, astro]
        ---

        Every developer eventually builds a personal site. This is mine.

        ```ts
        const greeting = 'Hello, world';
        ```
        """;

    private const string DraftFile = """
        ---
        title: Example draft (not published)
        description: Set draft to false to publish a post.
        date: 2026-09-21
        tags: [meta]
        draft: true
        ---

        Posts with `draft: true` are hidden from the site and RSS feed.
        """;

    private const string UpdateFile = """
        ---
        title: Launched this website
        date: 2026-09-20
        ---

        Portfolio, blog and updates in one place.
        """;

    private static string Normalised(string raw) => raw.Replace("\r\n", "\n").TrimEnd('\n') + "\n";

    [Theory]
    [InlineData("projects", ProjectFile)]
    [InlineData("blog", BlogFile)]
    [InlineData("blog", DraftFile)]
    [InlineData("updates", UpdateFile)]
    public void Opening_and_saving_an_unchanged_file_reproduces_it_exactly(string collection, string original)
    {
        var (frontmatter, body) = PortfolioContent.Parse(original);

        var rewritten = PortfolioContent.Compose(collection, frontmatter, body);

        rewritten.Should().Be(Normalised(original), "an edit must not reformat lines the author did not change");
    }

    [Fact]
    public void Parsing_gives_typed_values_and_keeps_dates_as_text()
    {
        var (frontmatter, body) = PortfolioContent.Parse(ProjectFile);

        frontmatter["title"].Should().Be("Articles");
        frontmatter["date"].Should().Be("2026-08-01");
        frontmatter["featured"].Should().Be(true);
        ((List<object?>)frontmatter["tech"]!).Should().Equal("ASP.NET Core", "React", "TipTap", "SQL");
        body.Should().StartWith("Articles is a content publishing platform");
    }

    [Fact]
    public void Changing_one_field_changes_only_that_line()
    {
        var (frontmatter, body) = PortfolioContent.Parse(BlogFile);
        frontmatter["tags"] = new List<object?> { "meta", "astro", "writing" };

        var after = PortfolioContent.Compose("blog", frontmatter, body);

        var changed = Normalised(BlogFile).Split('\n').Zip(after.Split('\n')).Where(p => p.First != p.Second).ToList();
        changed.Should().ContainSingle();
        changed[0].Second.Should().Be("tags: [meta, astro, writing]");
    }

    // ---- Writing ------------------------------------------------------------------------

    private static Dictionary<string, object?> Blog(string title = "A post", string description = "About a post", string date = "2026-10-01") => new()
    {
        ["title"] = title, ["description"] = description, ["date"] = date
    };

    [Fact]
    public void A_new_entry_is_written_in_canonical_order_regardless_of_input_order()
    {
        var scrambled = new Dictionary<string, object?>
        {
            ["draft"] = true,
            ["tags"] = new List<object?> { "one", "two" },
            ["date"] = "2026-10-01",
            ["description"] = "Desc",
            ["title"] = "Title"
        };

        PortfolioContent.Compose("blog", scrambled, "Body").Should().Be(
            "---\ntitle: Title\ndescription: Desc\ndate: 2026-10-01\ntags: [one, two]\ndraft: true\n---\n\nBody\n");
    }

    [Fact]
    public void False_flags_and_empty_lists_are_left_out_as_the_site_defaults_them()
    {
        var entry = Blog();
        entry["draft"] = false;
        entry["tags"] = new List<object?>();

        PortfolioContent.Compose("blog", entry, "x").Should().NotContain("draft").And.NotContain("tags");
    }

    [Theory]
    [InlineData("Plain title", "Plain title")]
    [InlineData("Ratio: 3:1", "\"Ratio: 3:1\"")]
    [InlineData("Ends with colon:", "\"Ends with colon:\"")]
    [InlineData("Use # for headings", "\"Use # for headings\"")]
    [InlineData("#hashtag first", "\"#hashtag first\"")]
    [InlineData("- looks like a list", "\"- looks like a list\"")]
    [InlineData("[bracketed] start", "\"[bracketed] start\"")]
    [InlineData("*starred", "\"*starred\"")]
    [InlineData("true", "\"true\"")]
    [InlineData("No", "\"No\"")]
    [InlineData("y", "\"y\"")]
    [InlineData("off", "\"off\"")]
    [InlineData("null", "\"null\"")]
    [InlineData("2026", "\"2026\"")]
    [InlineData("1.5", "\"1.5\"")]
    [InlineData("2026-09-21", "\"2026-09-21\"")]
    [InlineData("She said \"hi\"", "She said \"hi\"")]
    [InlineData("Back\\slash", "Back\\slash")]
    public void Text_is_quoted_exactly_when_YAML_would_otherwise_misread_it(string title, string written)
    {
        var markdown = PortfolioContent.Compose("blog", Blog(title: title), "Body");

        markdown.Split('\n')[1].Should().Be($"title: {written}");
    }

    [Theory]
    [InlineData("Ratio: 3:1")]
    [InlineData("true")]
    [InlineData("2026")]
    [InlineData("- dash")]
    [InlineData("a, b")]
    [InlineData("quote \" inside: and #")]
    [InlineData("  padded  ")]
    [InlineData("ünïcode — dash ✓")]
    public void Whatever_is_written_reads_back_as_the_same_text(string title)
    {
        var markdown = PortfolioContent.Compose("blog", Blog(title: title), "Body");

        var (frontmatter, _) = PortfolioContent.Parse(markdown);

        frontmatter["title"].Should().Be(title.Trim());
    }

    [Fact]
    public void List_items_that_contain_commas_or_brackets_are_quoted_so_the_list_stays_intact()
    {
        var entry = Blog();
        entry["tags"] = new List<object?> { "a, b", "c]", "plain" };

        var markdown = PortfolioContent.Compose("blog", entry, "x");
        var (frontmatter, _) = PortfolioContent.Parse(markdown);

        ((List<object?>)frontmatter["tags"]!).Should().Equal("a, b", "c]", "plain");
    }

    [Fact]
    public void Duplicate_tags_are_collapsed_ignoring_case()
    {
        var entry = Blog();
        entry["tags"] = new List<object?> { "Astro", "astro", "meta" };

        PortfolioContent.Compose("blog", entry, "x").Should().Contain("tags: [Astro, meta]");
    }

    [Fact]
    public void A_project_with_impact_and_links_is_written_in_the_sites_nested_style()
    {
        var entry = new Dictionary<string, object?>
        {
            ["title"] = "Tool", ["summary"] = "Does things", ["date"] = "2026-05-01",
            ["tech"] = new List<object?> { "C#" }, ["featured"] = true,
            ["impact"] = new Dictionary<string, object?> { ["value"] = "40%", ["label"] = "faster builds" },
            ["repo"] = "https://github.com/me/tool", ["demo"] = "https://tool.example.com"
        };

        var markdown = PortfolioContent.Compose("projects", entry, "Details");
        markdown.Should().Contain("impact:\n  value: 40%\n  label: faster builds\n");

        var (parsed, _) = PortfolioContent.Parse(markdown);
        ((Dictionary<string, object?>)parsed["impact"]!)["value"].Should().Be("40%");
    }

    [Fact]
    public void Unknown_front_matter_keys_survive_an_edit()
    {
        var (frontmatter, body) = PortfolioContent.Parse("---\ntitle: T\ndate: 2026-01-02\ncustom: hello\nextras: [a, b]\n---\n\nBody\n");
        frontmatter["title"] = "Changed";

        var after = PortfolioContent.Compose("updates", frontmatter, body);

        after.Should().Contain("title: Changed").And.Contain("custom: hello").And.Contain("extras: [a, b]");
    }

    [Fact]
    public void JSON_values_from_the_web_are_understood()
    {
        var json = JsonSerializer.Deserialize<Dictionary<string, object?>>(
            """{"title":"From JSON","description":"d","date":"2026-02-03","tags":["x","z"],"draft":true}""")!;

        var markdown = PortfolioContent.Compose("blog", json, "Body");

        markdown.Should().Contain("title: From JSON").And.Contain("tags: [x, z]").And.Contain("draft: true");
    }

    [Fact]
    public void The_body_is_normalised_to_unix_line_endings_with_one_trailing_newline()
    {
        var markdown = PortfolioContent.Compose("blog", Blog(), "Line one\r\n\r\nLine two\r\n\r\n\r\n");

        markdown.Should().EndWith("Line one\n\nLine two\n");
        markdown.Should().NotContain("\r");
    }

    [Fact]
    public void An_empty_body_still_produces_a_valid_file()
    {
        PortfolioContent.Compose("blog", Blog(), "").Should().EndWith("---\n");
    }

    // ---- Rejecting what would break the site's build ----------------------------------------

    public static IEnumerable<object[]> BadEntries()
    {
        yield return ["blog", new Dictionary<string, object?> { ["description"] = "d", ["date"] = "2026-01-01" }, "title is required"];
        yield return ["blog", new Dictionary<string, object?> { ["title"] = "t", ["date"] = "2026-01-01" }, "description is required"];
        yield return ["blog", new Dictionary<string, object?> { ["title"] = "t", ["description"] = "d" }, "date is required"];
        yield return ["blog", new Dictionary<string, object?> { ["title"] = "t", ["description"] = "d", ["date"] = "01/02/2026" }, "YYYY-MM-DD"];
        yield return ["blog", new Dictionary<string, object?> { ["title"] = "t", ["description"] = "d", ["date"] = "2026-02-30" }, "real date"];
        yield return ["blog", new Dictionary<string, object?> { ["title"] = "t", ["description"] = "d", ["date"] = "1850-01-01" }, "real date"];
        yield return ["blog", new Dictionary<string, object?> { ["title"] = new string('x', 201), ["description"] = "d", ["date"] = "2026-01-01" }, "longer than 200"];
        yield return ["blog", new Dictionary<string, object?> { ["title"] = "two\nlines", ["description"] = "d", ["date"] = "2026-01-01" }, "single line"];
        yield return ["blog", new Dictionary<string, object?> { ["title"] = "t", ["description"] = "d", ["date"] = "2026-01-01", ["draft"] = "yes" }, "true or false"];
        yield return ["blog", new Dictionary<string, object?> { ["title"] = "t", ["description"] = "d", ["date"] = "2026-01-01", ["tags"] = "not-a-list" }, "must be a list"];
        yield return ["blog", new Dictionary<string, object?> { ["title"] = "t", ["description"] = "d", ["date"] = "2026-01-01", ["tags"] = Enumerable.Range(0, 11).Select(i => (object?)$"t{i}").ToList() }, "at most 10"];
        yield return ["projects", new Dictionary<string, object?> { ["title"] = "t", ["summary"] = "s", ["date"] = "2026-01-01", ["repo"] = "javascript:alert(1)" }, "http:// or https://"];
        yield return ["projects", new Dictionary<string, object?> { ["title"] = "t", ["summary"] = "s", ["date"] = "2026-01-01", ["demo"] = "not a url" }, "http:// or https://"];
        yield return ["projects", new Dictionary<string, object?> { ["title"] = "t", ["summary"] = "s", ["date"] = "2026-01-01", ["impact"] = new Dictionary<string, object?> { ["value"] = "only value" } }, "both a value and a label"];
        yield return ["updates", new Dictionary<string, object?> { ["title"] = "t", ["date"] = "2026-01-01", ["link"] = "javascript:alert(1)" }, "http(s) link or a path"];
        yield return ["updates", new Dictionary<string, object?> { ["title"] = "t", ["date"] = "2026-01-01", ["link"] = "//evil.example" }, "http(s) link or a path"];
    }

    [Theory]
    [MemberData(nameof(BadEntries))]
    public void An_entry_that_would_fail_the_sites_build_is_refused_with_a_clear_reason(string collection, Dictionary<string, object?> entry, string reason)
    {
        var act = () => PortfolioContent.Compose(collection, entry, "Body");

        act.Should().Throw<DomainException>().WithMessage($"*{reason}*");
    }

    [Fact]
    public void Every_problem_is_reported_at_once()
    {
        var act = () => PortfolioContent.Compose("blog", new Dictionary<string, object?>(), "Body");

        act.Should().Throw<DomainException>().Which.Message.Should()
            .Contain("title is required").And.Contain("description is required").And.Contain("date is required");
    }

    [Fact]
    public void An_oversized_body_is_refused()
    {
        var act = () => PortfolioContent.Compose("blog", Blog(), new string('x', PortfolioContent.MaxBodyLength + 1));

        act.Should().Throw<DomainException>().WithMessage("*body is longer*");
    }

    [Fact]
    public void An_unknown_collection_is_not_found()
    {
        var act = () => PortfolioContent.Compose("secrets", Blog(), "x");

        act.Should().Throw<NotFoundException>();
    }

    [Theory]
    [InlineData("bad key!")]
    [InlineData("../etc")]
    [InlineData("1starts-with-digit")]
    public void Unknown_keys_must_be_plain_identifiers(string key)
    {
        var entry = Blog();
        entry[key] = "value";

        var act = () => PortfolioContent.Compose("blog", entry, "x");

        act.Should().Throw<DomainException>().WithMessage("*not a valid field name*");
    }

    // ---- Reading files that are not as expected ----------------------------------------------

    [Theory]
    [InlineData("no front matter at all")]
    [InlineData("---\ntitle: never closed\n")]
    [InlineData("---\n: : :\n  - broken [\n---\nbody")]
    public void A_file_whose_front_matter_cannot_be_read_is_reported_rather_than_crashing(string text)
    {
        var act = () => PortfolioContent.Parse(text);

        act.Should().Throw<DomainException>();
    }

    [Fact]
    public void Windows_line_endings_and_a_byte_order_mark_are_tolerated()
    {
        var (frontmatter, body) = PortfolioContent.Parse("﻿---\r\ntitle: Crlf\r\ndate: 2026-01-01\r\n---\r\n\r\nBody text\r\n");

        frontmatter["title"].Should().Be("Crlf");
        body.Should().Be("Body text\n");
    }

    [Fact]
    public void A_file_with_empty_front_matter_parses_to_nothing_and_keeps_its_body()
    {
        var (frontmatter, body) = PortfolioContent.Parse("---\n---\nJust a body\n");

        frontmatter.Should().BeEmpty();
        body.Should().Be("Just a body\n");
    }

    [Theory]
    [InlineData("hello-world", true)]
    [InlineData("a", true)]
    [InlineData("post-2026", true)]
    [InlineData("Hello", false)]
    [InlineData("-leading", false)]
    [InlineData("trailing-", false)]
    [InlineData("has space", false)]
    [InlineData("../escape", false)]
    [InlineData("dots.md", false)]
    [InlineData("", false)]
    public void File_names_are_restricted_to_safe_lowercase_slugs(string slug, bool valid) =>
        PortfolioContent.IsValidSlug(slug).Should().Be(valid);
}
