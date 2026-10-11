using FluentAssertions;
using Articles.Application.Common;
using Xunit;

namespace Articles.Tests.Common;

public class SlugGeneratorTests
{
    [Theory]
    [InlineData("Hello World", "hello-world")]
    [InlineData("  Trim me  ", "trim-me")]
    [InlineData("Punctuation, everywhere!", "punctuation-everywhere")]
    [InlineData("Multiple   spaces", "multiple-spaces")]
    [InlineData("Already-slugged", "already-slugged")]
    [InlineData("Numbers 123 stay", "numbers-123-stay")]
    public void Generate_produces_url_safe_slugs(string input, string expected) =>
        SlugGenerator.Generate(input).Should().Be(expected);

    [Fact]
    public void Generate_strips_accents_so_equivalent_titles_collapse() =>
        SlugGenerator.Generate("Café résumé").Should().Be("cafe-resume");

    [Fact]
    public void Generate_returns_empty_for_input_with_no_alphanumerics() =>
        SlugGenerator.Generate("!!! ???").Should().BeEmpty();

    [Fact]
    public void Generate_truncates_long_titles_without_leaving_a_trailing_dash()
    {
        var slug = SlugGenerator.Generate(string.Join(' ', Enumerable.Repeat("word", 40)));

        slug.Length.Should().BeLessThanOrEqualTo(80);
        slug.Should().NotEndWith("-");
    }

    [Fact]
    public void WithCode_keeps_the_title_and_appends_a_short_code()
    {
        var coded = SlugGenerator.WithCode("today-for-tomorrow");

        coded.Should().MatchRegex("^today-for-tomorrow-[23456789a-hjkmnp-z]{5}$");
        coded.Should().NotBe(SlugGenerator.WithCode("today-for-tomorrow"));
    }

    [Fact]
    public void A_code_never_uses_characters_that_are_easy_to_misread()
    {
        var codes = string.Concat(Enumerable.Range(0, 400).Select(_ => SlugGenerator.WithCode("x")[2..]));

        codes.Should().NotContainAny("0", "o", "1", "l", "i");
        codes.Should().MatchRegex("^[a-z0-9]+$", "an address is lower case with no punctuation");
    }

    [Fact]
    public void Codes_are_spread_out_rather_than_repeating()
    {
        var codes = Enumerable.Range(0, 2000).Select(_ => SlugGenerator.WithCode("x")).ToHashSet();

        codes.Count.Should().BeGreaterThan(1990);
    }

    [Fact]
    public void WithCode_on_an_empty_title_is_just_the_code()
    {
        SlugGenerator.WithCode("").Should().MatchRegex("^[23456789a-hjkmnp-z]{5}$");
    }

    [Fact]
    public void WithSuffix_keeps_the_base_and_appends_a_discriminator()
    {
        var suffixed = SlugGenerator.WithSuffix("my-post");

        suffixed.Should().StartWith("my-post-");
        suffixed.Should().NotBe(SlugGenerator.WithSuffix("my-post"));
    }
}
