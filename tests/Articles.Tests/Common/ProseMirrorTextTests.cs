using FluentAssertions;
using Articles.Application.Common;
using Articles.Domain.Exceptions;
using Xunit;

namespace Articles.Tests.Common;

public class ProseMirrorTextTests
{
    [Fact]
    public void Extract_flattens_paragraphs_into_plain_text()
    {
        var json = TestDatabase.Document("First paragraph.", "Second paragraph.");

        ProseMirrorText.Extract(json).Should().Be("First paragraph. Second paragraph.");
    }

    [Fact]
    public void Extract_walks_nested_marks_and_inline_nodes()
    {
        const string json = """
        {"type":"doc","content":[{"type":"paragraph","content":[
          {"type":"text","text":"Plain "},
          {"type":"text","marks":[{"type":"bold"}],"text":"bold"},
          {"type":"text","text":" tail"}]}]}
        """;

        ProseMirrorText.Extract(json).Should().Be("Plain bold tail");
    }

    [Fact]
    public void Extract_includes_image_alt_text_so_illustrated_posts_stay_searchable()
    {
        const string json = """
        {"type":"doc","content":[{"type":"image","attrs":{"src":"https://example.com/a.png","alt":"a red bicycle"}}]}
        """;

        ProseMirrorText.Extract(json).Should().Contain("a red bicycle");
    }

    [Fact]
    public void Extract_returns_empty_for_an_empty_document() =>
        ProseMirrorText.Extract("""{"type":"doc","content":[]}""").Should().BeEmpty();

    [Fact]
    public void Extract_rejects_content_that_is_not_valid_json()
    {
        var act = () => ProseMirrorText.Extract("not json at all");

        act.Should().Throw<DomainException>().WithMessage("*valid editor document*");
    }
}
