using FluentAssertions;
using Articles.Application.Posts.Dtos;
using Articles.Application.Posts.Validators;
using Articles.Application.Users.Dtos;
using Articles.Application.Users.Validators;
using Articles.Domain.Common;
using Articles.Domain.Entities;
using Articles.Domain.Exceptions;
using Xunit;

namespace Articles.Tests.Common;

public class UrlRulesTests
{
    [Fact]
    public void An_uploaded_picture_has_one_exact_address_shape()
    {
        var id = Guid.NewGuid();

        UrlRules.StoredImagePath(id).Should().Be($"/api/v1/images/{id}");
        UrlRules.IsStoredImagePath(UrlRules.StoredImagePath(id)).Should().BeTrue();
    }

    [Theory]
    [InlineData("/api/v1/images/")]
    [InlineData("/api/v1/images/0b6f8f0e-2f0b-4a53-9a3e-0e6a1f4f7c11/extra")]
    [InlineData("/api/v1/images/0b6f8f0e-2f0b-4a53-9a3e-0e6a1f4f7c11?x=1")]
    [InlineData("/api/v1/images/../admin/stats")]
    [InlineData("//evil.example/api/v1/images/0b6f8f0e-2f0b-4a53-9a3e-0e6a1f4f7c11")]
    [InlineData("https://evil.example/api/v1/images/0b6f8f0e-2f0b-4a53-9a3e-0e6a1f4f7c11x")]
    [InlineData("/api/v1/posts")]
    [InlineData("")]
    [InlineData(null)]
    public void Nothing_else_passes_as_an_uploaded_picture(string? value) =>
        UrlRules.IsStoredImagePath(value).Should().BeFalse();

    [Theory]
    [InlineData("https://example.com")]
    [InlineData("https://example.com/a/b?c=d#e")]
    [InlineData("  https://example.com  ")]
    [InlineData("http://example.com")]
    public void Http_and_https_links_are_accepted(string url) =>
        UrlRules.IsHttpOrHttps(url).Should().BeTrue();

    [Theory]
    [InlineData("javascript:alert(document.domain)")]
    [InlineData("JAVASCRIPT:alert(1)")]
    [InlineData("  javascript:alert(1)")]
    [InlineData("data:text/html,<script>alert(1)</script>")]
    [InlineData("file:///etc/passwd")]
    [InlineData("vbscript:msgbox(1)")]
    [InlineData("ftp://example.com/file")]
    [InlineData("//example.com/protocol-relative")]
    [InlineData("/relative/path")]
    [InlineData("example.com")]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void Anything_else_is_rejected(string? url)
    {
        UrlRules.IsHttpOrHttps(url).Should().BeFalse();
        UrlRules.IsHttps(url).Should().BeFalse();
    }

    [Fact]
    public void Images_must_be_https_so_they_are_not_blocked_as_mixed_content()
    {
        UrlRules.IsHttps("https://example.com/a.png").Should().BeTrue();
        UrlRules.IsHttps("http://example.com/a.png").Should().BeFalse();
    }

    [Fact]
    public void The_profile_validator_rejects_a_javascript_website()
    {
        var result = new UpdateProfileRequestValidator().Validate(
            new UpdateProfileRequest("Ada", null, null, "javascript:alert(1)"));

        result.Errors.Should().ContainSingle(e => e.PropertyName == "WebsiteUrl");
    }

    [Fact]
    public void The_profile_validator_rejects_an_http_avatar()
    {
        var result = new UpdateProfileRequestValidator().Validate(
            new UpdateProfileRequest("Ada", null, "http://example.com/a.png", null));

        result.Errors.Should().ContainSingle(e => e.PropertyName == "AvatarUrl");
    }

    [Fact]
    public void The_profile_validator_accepts_empty_and_valid_links()
    {
        var validator = new UpdateProfileRequestValidator();

        validator.Validate(new UpdateProfileRequest("Ada", null, null, null)).IsValid.Should().BeTrue();
        validator.Validate(new UpdateProfileRequest("Ada", null, "https://example.com/a.png", "http://example.com")).IsValid.Should().BeTrue();
    }

    [Fact]
    public void The_post_validators_reject_a_non_https_cover()
    {
        var create = new CreatePostRequestValidator().Validate(
            new CreatePostRequest("T", null, "{}", "javascript:alert(1)", null));
        var update = new UpdatePostRequestValidator().Validate(
            new UpdatePostRequest("T", null, "{}", "http://example.com/a.png", null));

        create.Errors.Should().ContainSingle(e => e.PropertyName == "CoverImageUrl");
        update.Errors.Should().ContainSingle(e => e.PropertyName == "CoverImageUrl");
    }

    [Fact]
    public void The_domain_refuses_bad_urls_even_if_validation_is_bypassed()
    {
        var user = new User("a@example.com", "ada", "Ada", "hash");

        var profile = () => user.UpdateProfile("Ada", null, null, "javascript:alert(1)");
        var avatar = () => user.UpdateProfile("Ada", null, "data:image/png;base64,AAAA", null);
        var cover = () => new Post(Guid.NewGuid(), "T", null, "{}", "text", "javascript:alert(1)");

        profile.Should().Throw<DomainException>();
        avatar.Should().Throw<DomainException>();
        cover.Should().Throw<DomainException>();
    }
}
