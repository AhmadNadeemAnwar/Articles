using System.Text.Json;
using FluentAssertions;
using Articles.Application.Common;
using Articles.Domain.Exceptions;
using Xunit;

namespace Articles.Tests.Common;

public class PostContentTests
{
    private const string ImageId = "0b6f8f0e-2f0b-4a53-9a3e-0e6a1f4f7c11";

    private static string Sections(params string[] sections) =>
        $$"""{"type":"sections","content":[{{string.Join(",", sections)}}]}""";

    private static string Text(string words) =>
        $$"""{"type":"textSection","content":[{"type":"paragraph","content":[{"type":"text","text":"{{words}}"}]}]}""";

    private static string Image(string imageId = ImageId, string alt = "A diagram", string caption = "Figure 1") =>
        JsonSerializer.Serialize(new { type = "imageSection", attrs = new { imageId, alt, caption } });

    private static string References(params (string Title, string Url)[] items) =>
        JsonSerializer.Serialize(new { type = "referencesSection", attrs = new { items = items.Select(i => new { title = i.Title, url = i.Url }) } });

    [Fact]
    public void The_original_single_document_is_still_accepted()
    {
        var act = () => PostContent.Validate(TestDatabase.Document("Written before sections existed."));

        act.Should().NotThrow();
    }

    [Fact]
    public void A_post_made_of_text_image_and_reference_sections_is_accepted()
    {
        var json = Sections(Text("Opening."), Image(), References(("The paper", "https://example.com/paper"), ("A book with no link", "")));

        var act = () => PostContent.Validate(json);

        act.Should().NotThrow();
    }

    [Fact]
    public void An_empty_list_of_sections_is_accepted_so_a_blank_draft_can_be_saved()
    {
        var act = () => PostContent.Validate(Sections());

        act.Should().NotThrow();
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("not json")]
    [InlineData("[]")]
    [InlineData("""{"type":"somethingElse","content":[]}""")]
    [InlineData("""{"type":"sections"}""")]
    [InlineData("""{"type":"sections","content":{}}""")]
    public void Anything_that_is_not_a_post_body_is_rejected(string json)
    {
        var act = () => PostContent.Validate(json);

        act.Should().Throw<DomainException>();
    }

    [Theory]
    [InlineData("""{"type":"scriptSection"}""")]
    [InlineData("""{"type":"paragraph"}""")]
    [InlineData("\"just a string\"")]
    public void A_section_of_an_unknown_kind_is_rejected(string section)
    {
        var act = () => PostContent.Validate(Sections(section));

        act.Should().Throw<DomainException>().WithMessage("*text, image and reference*");
    }

    [Theory]
    [InlineData("")]
    [InlineData("not-a-guid")]
    [InlineData("https://evil.example/tracker.png")]
    public void An_image_section_must_point_at_an_uploaded_picture(string imageId)
    {
        var act = () => PostContent.Validate(Sections(Image(imageId)));

        act.Should().Throw<DomainException>().WithMessage("*no picture*");
    }

    [Fact]
    public void An_image_section_with_no_attributes_is_rejected()
    {
        var act = () => PostContent.Validate(Sections("""{"type":"imageSection"}"""));

        act.Should().Throw<DomainException>();
    }

    [Fact]
    public void A_picture_may_carry_its_size_so_pages_can_hold_its_place()
    {
        var withSize = JsonSerializer.Serialize(new { type = "imageSection", attrs = new { imageId = ImageId, alt = "A chart", caption = "", width = 1600, height = 900 } });

        ((Action)(() => PostContent.Validate(Sections(withSize)))).Should().NotThrow();
        ((Action)(() => PostContent.Validate(Sections(Image())))).Should().NotThrow("older posts have no size at all");
    }

    [Theory]
    [InlineData("""{"imageId":"0b6f8f0e-2f0b-4a53-9a3e-0e6a1f4f7c11","width":1600}""")]
    [InlineData("""{"imageId":"0b6f8f0e-2f0b-4a53-9a3e-0e6a1f4f7c11","height":900}""")]
    [InlineData("""{"imageId":"0b6f8f0e-2f0b-4a53-9a3e-0e6a1f4f7c11","width":0,"height":900}""")]
    [InlineData("""{"imageId":"0b6f8f0e-2f0b-4a53-9a3e-0e6a1f4f7c11","width":-5,"height":900}""")]
    [InlineData("""{"imageId":"0b6f8f0e-2f0b-4a53-9a3e-0e6a1f4f7c11","width":1600.5,"height":900}""")]
    [InlineData("""{"imageId":"0b6f8f0e-2f0b-4a53-9a3e-0e6a1f4f7c11","width":"1600","height":"900"}""")]
    [InlineData("""{"imageId":"0b6f8f0e-2f0b-4a53-9a3e-0e6a1f4f7c11","width":999999,"height":900}""")]
    public void A_size_that_is_partial_or_not_a_real_size_is_rejected(string attrs)
    {
        var act = () => PostContent.Validate(Sections($$"""{"type":"imageSection","attrs":{{attrs}}}"""));

        act.Should().Throw<DomainException>().WithMessage("*size*");
    }

    [Fact]
    public void An_overlong_caption_is_rejected()
    {
        var act = () => PostContent.Validate(Sections(Image(caption: new string('c', PostContent.MaxCaptionLength + 1))));

        act.Should().Throw<DomainException>().WithMessage("*at most*");
    }

    [Theory]
    [InlineData("javascript:alert(1)")]
    [InlineData("data:text/html,hello")]
    [InlineData("//example.com/protocol-relative")]
    [InlineData("example.com/no-scheme")]
    public void A_reference_link_must_be_a_real_web_address(string url)
    {
        var act = () => PostContent.Validate(Sections(References(("A source", url))));

        act.Should().Throw<DomainException>().WithMessage("*http*");
    }

    [Fact]
    public void A_reference_needs_a_title()
    {
        var act = () => PostContent.Validate(Sections(References(("  ", "https://example.com"))));

        act.Should().Throw<DomainException>().WithMessage("*needs a title*");
    }

    [Fact]
    public void A_reference_section_that_is_not_a_list_is_rejected()
    {
        var act = () => PostContent.Validate(Sections("""{"type":"referencesSection","attrs":{"items":"nope"}}"""));

        act.Should().Throw<DomainException>();
    }

    [Fact]
    public void There_is_a_ceiling_on_sections_and_on_sources()
    {
        var tooManySections = Sections(Enumerable.Repeat(Text("x"), PostContent.MaxSections + 1).ToArray());
        var tooManySources = Sections(References(Enumerable.Repeat(("A source", "https://example.com"), PostContent.MaxReferences + 1).ToArray()));

        ((Action)(() => PostContent.Validate(tooManySections))).Should().Throw<DomainException>();
        ((Action)(() => PostContent.Validate(tooManySources))).Should().Throw<DomainException>();
    }

    [Fact]
    public void A_body_past_the_size_limit_is_rejected_before_it_is_parsed()
    {
        var act = () => PostContent.Validate(new string('x', PostContent.MaxLength + 1));

        act.Should().Throw<DomainException>().WithMessage("*too long*");
    }

    // ---- What search and excerpts see -------------------------------------------------------

    [Fact]
    public void Plain_text_is_drawn_from_every_kind_of_section_in_order()
    {
        var json = Sections(
            Text("Opening words."),
            Image(alt: "A chart of results", caption: "Figure 1"),
            Text("Closing words."),
            References(("The original paper", "https://example.com/paper")));

        ProseMirrorText.Extract(json).Should().Be("Opening words. A chart of results Figure 1 Closing words. The original paper");
    }

    [Fact]
    public void Reference_links_and_image_ids_never_leak_into_the_searchable_text()
    {
        var json = Sections(Image(), References(("A source", "https://example.com/secret-path")));

        var text = ProseMirrorText.Extract(json);

        text.Should().NotContain("example.com").And.NotContain(ImageId);
    }

    private static string SizedImage(object? size) =>
        JsonSerializer.Serialize(new { type = "imageSection", attrs = new { imageId = ImageId, alt = "A chart", caption = "", size } });

    [Theory]
    [InlineData("small")]
    [InlineData("medium")]
    [InlineData("full")]
    [InlineData(null)]
    public void A_picture_can_be_shown_small_medium_or_full_width(string? size)
    {
        var act = () => PostContent.Validate(Sections(SizedImage(size)));

        act.Should().NotThrow();
    }

    [Theory]
    [InlineData("huge")]
    [InlineData("SMALL")]
    [InlineData("50%")]
    [InlineData(40)]
    public void Any_other_picture_size_is_refused(object size)
    {
        var act = () => PostContent.Validate(Sections(SizedImage(size)));

        act.Should().Throw<DomainException>().WithMessage("*small, medium or full width*");
    }
}
