using FluentAssertions;
using Articles.Domain.Entities;
using Articles.Domain.Enums;
using Articles.Domain.Exceptions;
using Xunit;

namespace Articles.Tests.Domain;

public class PostTests
{
    private static Post NewPost(string plainText = "Some body text.", string title = "A title") =>
        new(Guid.NewGuid(), title, null, TestDatabase.Document(plainText), plainText);

    [Fact]
    public void New_post_starts_as_a_draft_with_no_slug()
    {
        var post = NewPost();

        post.Status.Should().Be(PostStatus.Draft);
        post.Slug.Should().BeNull();
        post.PublishedAt.Should().BeNull();
    }

    [Theory]
    [InlineData(1, 1)]
    [InlineData(225, 1)]
    [InlineData(226, 2)]
    [InlineData(1000, 5)]
    public void Reading_time_rounds_up_and_never_reports_zero(int wordCount, int expectedMinutes)
    {
        var post = NewPost(string.Join(' ', Enumerable.Repeat("word", wordCount)));

        post.ReadingTimeMinutes.Should().Be(expectedMinutes);
    }

    [Fact]
    public void Publish_assigns_the_slug_and_timestamp()
    {
        var post = NewPost();

        post.Publish("a-title");

        post.Status.Should().Be(PostStatus.Published);
        post.Slug.Should().Be("a-title");
        post.PublishedAt.Should().NotBeNull();
    }

    [Fact]
    public void Republishing_keeps_the_original_slug_so_inbound_links_survive_a_retitle()
    {
        var post = NewPost();
        post.Publish("original-title");
        var publishedAt = post.PublishedAt;

        post.Deactivate();
        post.UpdateDraft("A completely different title", null, TestDatabase.Document("Body"), "Body", null);
        post.Publish("a-completely-different-title");

        post.Slug.Should().Be("original-title");
        post.PublishedAt.Should().Be(publishedAt);
    }

    [Fact]
    public void A_post_can_move_between_all_three_states()
    {
        var post = NewPost();

        post.Publish("a-title");
        post.Deactivate();
        post.Status.Should().Be(PostStatus.Inactive);

        post.MoveToDraft();
        post.Status.Should().Be(PostStatus.Draft);

        post.Deactivate();
        post.Status.Should().Be(PostStatus.Inactive, "a draft can be switched off without ever being published");

        post.Publish("ignored-because-it-already-has-one");
        post.Status.Should().Be(PostStatus.Published);
        post.Slug.Should().Be("a-title");
    }

    [Fact]
    public void Moving_to_the_state_it_is_already_in_is_rejected()
    {
        var post = NewPost();

        ((Action)post.MoveToDraft).Should().Throw<DomainException>().WithMessage("*already a draft*");

        post.Deactivate();
        ((Action)post.Deactivate).Should().Throw<DomainException>().WithMessage("*already not active*");
    }

    [Fact]
    public void Only_a_published_post_is_visible_to_readers()
    {
        var authorId = Guid.NewGuid();
        var post = new Post(authorId, "Title", null, TestDatabase.Document("body"), "body");

        post.Publish("title");
        post.IsVisibleTo(null).Should().BeTrue();

        post.Deactivate();
        post.IsVisibleTo(null).Should().BeFalse("an inactive post is hidden even from someone holding its link");
        post.IsVisibleTo(Guid.NewGuid()).Should().BeFalse();
        post.IsVisibleTo(authorId).Should().BeTrue();
    }

    [Theory]
    [InlineData("/api/v1/images/0b6f8f0e-2f0b-4a53-9a3e-0e6a1f4f7c11", true)]
    [InlineData("https://example.com/cover.jpg", true)]
    [InlineData("http://example.com/cover.jpg", false)]
    [InlineData("/api/v1/images/../admin/stats", false)]
    [InlineData("/some/other/path.png", false)]
    public void A_cover_is_an_uploaded_picture_or_an_https_link(string cover, bool allowed)
    {
        var act = () => new Post(Guid.NewGuid(), "Title", null, TestDatabase.Document("body"), "body", cover);

        if (allowed) act.Should().NotThrow();
        else act.Should().Throw<DomainException>().WithMessage("*Cover image*");
    }

    [Fact]
    public void Reaction_totals_go_up_and_down_by_one_and_never_below_zero()
    {
        var post = NewPost();

        post.AddReaction(ReactionKind.Clap);
        post.AddReaction(ReactionKind.Clap);
        post.AddReaction(ReactionKind.Insightful);
        (post.ClapCount, post.InsightfulCount).Should().Be((2, 1));

        post.RemoveReaction(ReactionKind.Clap);
        post.RemoveReaction(ReactionKind.Insightful);
        post.RemoveReaction(ReactionKind.Insightful);
        (post.ClapCount, post.InsightfulCount).Should().Be((1, 0));
    }

    [Fact]
    public void Publishing_twice_is_rejected()
    {
        var post = NewPost();
        post.Publish("a-title");

        var act = () => post.Publish("a-title");

        act.Should().Throw<DomainException>().WithMessage("*already published*");
    }

    [Fact]
    public void An_empty_post_cannot_be_published()
    {
        var post = new Post(Guid.NewGuid(), "Title only", null, TestDatabase.Document(""), string.Empty);

        var act = () => post.Publish("title-only");

        act.Should().Throw<DomainException>().WithMessage("*empty post*");
    }

    [Fact]
    public void Title_is_required()
    {
        var act = () => new Post(Guid.NewGuid(), "   ", null, TestDatabase.Document("body"), "body");

        act.Should().Throw<DomainException>().WithMessage("*Title is required*");
    }

    [Fact]
    public void Drafts_are_visible_only_to_their_author()
    {
        var authorId = Guid.NewGuid();
        var post = new Post(authorId, "Draft", null, TestDatabase.Document("body"), "body");

        post.IsVisibleTo(authorId).Should().BeTrue();
        post.IsVisibleTo(Guid.NewGuid()).Should().BeFalse();
        post.IsVisibleTo(null).Should().BeFalse();
    }

    [Fact]
    public void Published_posts_are_visible_to_anonymous_readers()
    {
        var post = NewPost();
        post.Publish("a-title");

        post.IsVisibleTo(null).Should().BeTrue();
    }

    [Fact]
    public void EnsureOwnedBy_rejects_a_different_user()
    {
        var post = NewPost();

        var act = () => post.EnsureOwnedBy(Guid.NewGuid());

        act.Should().Throw<ForbiddenException>();
    }

    [Fact]
    public void Excerpt_prefers_the_subtitle_when_there_is_one()
    {
        var post = new Post(Guid.NewGuid(), "Title", "The subtitle", TestDatabase.Document("Body text"), "Body text");

        post.BuildExcerpt().Should().Be("The subtitle");
    }

    [Fact]
    public void Excerpt_truncates_on_a_word_boundary()
    {
        var body = string.Join(' ', Enumerable.Repeat("alpha", 100));
        var post = NewPost(body);

        var excerpt = post.BuildExcerpt(50);

        excerpt.Should().EndWith("...");
        excerpt.Should().NotContain("alph.");
        excerpt.Length.Should().BeLessThanOrEqualTo(54);
    }

    [Fact]
    public void Comment_count_never_goes_negative()
    {
        var post = NewPost();

        post.DecrementCommentCount();

        post.CommentCount.Should().Be(0);
    }
}
