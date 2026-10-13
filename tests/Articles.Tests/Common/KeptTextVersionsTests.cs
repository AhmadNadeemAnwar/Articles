using System.Text.Json;
using FluentAssertions;
using Articles.Application.Common;
using Articles.Domain.Exceptions;
using Xunit;

namespace Articles.Tests.Common;

/// <summary>
/// The writer's earlier wording kept beside the AI's improved text: textSection with a "textState" of Original, Improved, or
/// Improved_ and a time. Readers see only the newest; the older ones stay in the post and are not shown, searched or counted.
/// </summary>
public class KeptTextVersionsTests
{
    private const string Later = "Improved_2026-10-14T10:30:00Z";
    private const string Latest = "Improved_2026-10-15T08:00:00.1234567Z";

    private static string Sections(params string[] sections) => $$"""{"type":"sections","content":[{{string.Join(",", sections)}}]}""";

    private static string Text(string words, string? state = null) =>
        $$"""{"type":"textSection"{{(state is null ? "" : $$""","textState":"{{state}}" """)}},"content":[{"type":"paragraph","content":[{"type":"text","text":"{{words}}"}]}]}""";

    private static string Image() => """{"type":"imageSection","attrs":{"imageId":"0b6f8f0e-2f0b-4a53-9a3e-0e6a1f4f7c11","alt":"","caption":""}}""";

    private static void Valid(string json) => ((Action)(() => PostContent.Validate(json))).Should().NotThrow();
    private static void Invalid(string json) => ((Action)(() => PostContent.Validate(json))).Should().Throw<DomainException>();

    // ---- What is accepted -----------------------------------------------------------------------

    [Fact]
    public void An_original_followed_by_its_improved_text_is_accepted()
    {
        Valid(Sections(Text("one", "Original"), Text("two", "Improved")));
    }

    [Fact]
    public void Later_improvements_follow_with_the_time_they_were_made()
    {
        Valid(Sections(Text("a", "Original"), Text("b", "Improved"), Text("c", Later), Text("d", Latest)));
    }

    [Fact]
    public void Several_improved_sections_with_other_sections_between_the_groups_are_accepted()
    {
        Valid(Sections(Text("intro"), Text("a", "Original"), Text("b", "Improved"), Image(), Text("c", "Original"), Text("d", "Improved"), Text("end")));
    }

    [Fact]
    public void An_ordinary_section_with_no_state_is_still_ordinary()
    {
        Valid(Sections(Text("plain"), Text("also plain")));
    }

    // ---- What is refused ------------------------------------------------------------------------

    [Theory]
    [InlineData("Draft")]
    [InlineData("original")]
    [InlineData("Improved_")]
    [InlineData("Improved_yesterday")]
    [InlineData("Improved_2026-10-14")]
    [InlineData("Improved_2026-10-14T10:30:00")]
    [InlineData("Improved_2026-10-14T10:30:00Z<script>")]
    public void A_state_that_is_not_one_of_the_known_ones_is_refused(string state)
    {
        Invalid(Sections(Text("a", "Original"), Text("b", state)));
    }

    [Fact]
    public void A_state_that_is_not_text_is_refused()
    {
        Invalid("""{"type":"sections","content":[{"type":"textSection","textState":5,"content":[]}]}""");
    }

    [Fact]
    public void An_original_with_nothing_after_it_is_refused()
    {
        Invalid(Sections(Text("a", "Original")));
        Invalid(Sections(Text("a", "Original"), Image()));
        Invalid(Sections(Text("a", "Original"), Text("plain")));
    }

    [Fact]
    public void An_improved_text_that_does_not_follow_its_original_is_refused()
    {
        Invalid(Sections(Text("b", "Improved")));
        Invalid(Sections(Text("a"), Text("b", "Improved")));
        Invalid(Sections(Text("a", "Original"), Text("b", "Improved"), Text("c", "Improved")));
    }

    [Fact]
    public void A_timed_improvement_with_nothing_before_it_is_refused()
    {
        Invalid(Sections(Text("c", Later)));
        Invalid(Sections(Text("a"), Text("c", Later)));
    }

    // ---- What readers see -----------------------------------------------------------------------

    private static HashSet<int> Hidden(params string[] sections)
    {
        using var document = JsonDocument.Parse(Sections(sections));
        return PostContent.HiddenSections(document.RootElement.GetProperty("content"));
    }

    [Fact]
    public void Only_the_newest_of_a_group_is_visible()
    {
        Hidden(Text("intro"), Text("a", "Original"), Text("b", "Improved"), Text("c", Later), Text("d", Latest), Image(), Text("end"))
            .Should().BeEquivalentTo(new[] { 1, 2, 3 });
    }

    [Fact]
    public void A_group_of_just_an_original_and_its_improved_text_shows_the_improved_one()
    {
        Hidden(Text("a", "Original"), Text("b", "Improved")).Should().BeEquivalentTo(new[] { 0 });
    }

    [Fact]
    public void Ordinary_sections_are_never_hidden()
    {
        Hidden(Text("a"), Text("b"), Image()).Should().BeEmpty();
    }

    [Fact]
    public void Hidden_versions_are_not_searched_and_do_not_count_toward_the_excerpt_or_reading_time()
    {
        var plain = ProseMirrorText.Extract(Sections(Text("Intro."), Text("oldwording", "Original"), Text("midwording", "Improved"), Text("newwording", Later), Text("End.")));

        plain.Should().Be("Intro. newwording End.");
        plain.Should().NotContain("oldwording").And.NotContain("midwording");
    }

    [Fact]
    public void A_post_with_no_kept_versions_extracts_exactly_as_before()
    {
        ProseMirrorText.Extract(Sections(Text("One."), Text("Two."))).Should().Be("One. Two.");
    }
}
