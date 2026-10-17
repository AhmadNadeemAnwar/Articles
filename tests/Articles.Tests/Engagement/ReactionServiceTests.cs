using FluentAssertions;
using Articles.Application.Engagement;
using Articles.Application.Posts;
using Articles.Application.Posts.Dtos;
using Articles.Domain.Entities;
using Articles.Domain.Exceptions;
using Articles.Infrastructure.Persistence.Repositories;
using Xunit;

namespace Articles.Tests.Engagement;

public class VisitorKeyTests
{
    [Fact]
    public void The_same_browser_id_always_gives_the_same_key()
    {
        var id = Guid.NewGuid().ToString();

        VisitorKey.From(id).Should().Be(VisitorKey.From(id)).And.HaveLength(Reaction.VisitorKeyLength);
    }

    [Fact]
    public void However_the_id_is_written_it_is_the_same_visitor()
    {
        var id = Guid.NewGuid();

        VisitorKey.From(id.ToString("D").ToUpperInvariant()).Should().Be(VisitorKey.From($"  {id:D} "));
        VisitorKey.From(id.ToString("N")).Should().Be(VisitorKey.From(id.ToString("D")));
    }

    [Fact]
    public void Different_browsers_get_different_keys_and_the_id_itself_is_never_the_key()
    {
        var first = Guid.NewGuid().ToString();
        var second = Guid.NewGuid().ToString();

        VisitorKey.From(first).Should().NotBe(VisitorKey.From(second));
        VisitorKey.From(first).Should().NotContain(first.Replace("-", ""));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("not-an-id")]
    [InlineData("00000000-0000-0000-0000-000000000000")]
    [InlineData("'; DROP TABLE reactions; --")]
    public void Anything_that_is_not_a_real_id_identifies_nobody(string? value) =>
        VisitorKey.From(value).Should().BeNull();
}

public class BotDetectorTests
{
    [Theory]
    [InlineData("Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/141.0.0.0 Safari/537.36")]
    [InlineData("Mozilla/5.0 (iPhone; CPU iPhone OS 18_0 like Mac OS X) AppleWebKit/605.1.15 (KHTML, like Gecko) Version/18.0 Mobile/15E148 Safari/604.1")]
    [InlineData("Mozilla/5.0 (X11; Linux x86_64; rv:143.0) Gecko/20100101 Firefox/143.0")]
    public void Ordinary_browsers_are_readers(string userAgent) =>
        BotDetector.IsBot(userAgent).Should().BeFalse();

    [Theory]
    [InlineData("Mozilla/5.0 (compatible; Googlebot/2.1; +http://www.google.com/bot.html)")]
    [InlineData("Mozilla/5.0 (compatible; bingbot/2.0; +http://www.bing.com/bingbot.htm)")]
    [InlineData("facebookexternalhit/1.1 (+http://www.facebook.com/externalhit_uatext.php)")]
    [InlineData("WhatsApp/2.23.20.0")]
    [InlineData("LinkedInBot/1.0 (compatible; Mozilla/5.0; Apache-HttpClient +http://www.linkedin.com)")]
    [InlineData("Slackbot-LinkExpanding 1.0 (+https://api.slack.com/robots)")]
    [InlineData("Mozilla/5.0 (X11; Linux x86_64) AppleWebKit/537.36 (KHTML, like Gecko) HeadlessChrome/120.0.0.0 Safari/537.36")]
    [InlineData("curl/8.4.0")]
    [InlineData("python-requests/2.31.0")]
    [InlineData("UptimeRobot/2.0")]
    [InlineData("")]
    [InlineData(null)]
    public void Crawlers_previewers_and_scripts_are_not(string? userAgent) =>
        BotDetector.IsBot(userAgent).Should().BeTrue();
}

public class ReactionServiceTests : IDisposable
{
    private const string Browser = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) Chrome/141.0.0.0 Safari/537.36";

    private readonly TestDatabase _fixture = new();
    private readonly ManualTimeProvider _clock = new(new DateTimeOffset(2026, 6, 15, 22, 0, 0, TimeSpan.Zero));
    private readonly PostService _posts;
    private readonly ReactionService _reactions;

    public ReactionServiceTests()
    {
        _posts = new PostService(_fixture.Posts, _fixture.Tags, _fixture.Engagement, _fixture.Db);
        _reactions = new ReactionService(new ReactionRepository(_fixture.Db), _fixture.Posts, _clock);
    }

    private static string Visitor() => Guid.NewGuid().ToString();

    private async Task<(Guid PostId, User Author)> PublishedPostAsync(string handle = "author")
    {
        var author = await _fixture.AddUserAsync(handle);
        var draft = await _posts.CreateDraftAsync(new CreatePostRequest($"A post by {handle}", null, TestDatabase.Document("Body text here."), null, []), author.Id);
        await _posts.PublishAsync(draft.Id, author.Id);
        return (draft.Id, author);
    }

    private async Task<Post> StoredAsync(Guid postId)
    {
        _fixture.Db.ChangeTracker.Clear();
        return (await _fixture.Posts.GetByIdAsync(postId))!;
    }

    // ---- Reactions ----------------------------------------------------------------------------

    [Fact]
    public async Task One_click_claps_and_a_second_click_takes_it_back()
    {
        var (postId, _) = await PublishedPostAsync();
        var visitor = Visitor();

        var on = await _reactions.ToggleAsync(postId, visitor, "clap");
        var off = await _reactions.ToggleAsync(postId, visitor, "clap");

        on.Should().Be(new ReactionState(1, 0, true, false));
        off.Should().Be(new ReactionState(0, 0, false, false));
        (await StoredAsync(postId)).ClapCount.Should().Be(0);
        _fixture.Db.Reactions.Should().BeEmpty();
    }

    [Fact]
    public async Task Clicking_many_times_never_counts_one_visitor_more_than_once()
    {
        var (postId, _) = await PublishedPostAsync();
        var visitor = Visitor();

        ReactionState state = null!;
        for (var i = 0; i < 7; i++) state = await _reactions.ToggleAsync(postId, visitor, "clap");

        state.ClapCount.Should().Be(1, "seven clicks is on, off, on, off, on, off, on");
        (await StoredAsync(postId)).ClapCount.Should().Be(1);
    }

    [Fact]
    public async Task Each_visitor_counts_once_so_the_total_is_people_not_clicks()
    {
        var (postId, _) = await PublishedPostAsync();

        await _reactions.ToggleAsync(postId, Visitor(), "clap");
        await _reactions.ToggleAsync(postId, Visitor(), "clap");
        var third = await _reactions.ToggleAsync(postId, Visitor(), "clap");

        third.ClapCount.Should().Be(3);
    }

    [Fact]
    public async Task Clap_and_insightful_are_independent()
    {
        var (postId, _) = await PublishedPostAsync();
        var visitor = Visitor();

        await _reactions.ToggleAsync(postId, visitor, "clap");
        var both = await _reactions.ToggleAsync(postId, visitor, "insightful");
        var onlyInsightful = await _reactions.ToggleAsync(postId, visitor, "clap");

        both.Should().Be(new ReactionState(1, 1, true, true));
        onlyInsightful.Should().Be(new ReactionState(0, 1, false, true));
    }

    [Fact]
    public async Task One_visitors_undo_does_not_remove_anyone_elses_reaction()
    {
        var (postId, _) = await PublishedPostAsync();
        var first = Visitor();
        var second = Visitor();
        await _reactions.ToggleAsync(postId, first, "clap");
        await _reactions.ToggleAsync(postId, second, "clap");

        var afterUndo = await _reactions.ToggleAsync(postId, first, "clap");

        afterUndo.ClapCount.Should().Be(1);
        (await _reactions.GetAsync(postId, second)).Clapped.Should().BeTrue();
    }

    [Fact]
    public async Task A_reaction_to_one_post_does_not_appear_on_another()
    {
        var (first, _) = await PublishedPostAsync("ada");
        var (second, _) = await PublishedPostAsync("bob");
        var visitor = Visitor();

        await _reactions.ToggleAsync(first, visitor, "clap");

        (await _reactions.GetAsync(second, visitor)).Should().Be(new ReactionState(0, 0, false, false));
    }

    [Fact]
    public async Task Totals_are_shown_to_everyone_but_your_own_marks_only_to_you()
    {
        var (postId, _) = await PublishedPostAsync();
        var visitor = Visitor();
        await _reactions.ToggleAsync(postId, visitor, "insightful");

        (await _reactions.GetAsync(postId, visitor)).Should().Be(new ReactionState(0, 1, false, true));
        (await _reactions.GetAsync(postId, Visitor())).Should().Be(new ReactionState(0, 1, false, false));
        (await _reactions.GetAsync(postId, null)).Should().Be(new ReactionState(0, 1, false, false));
    }

    [Theory]
    [InlineData("Clap")]
    [InlineData("CLAP")]
    [InlineData(" insightful ")]
    public async Task The_kind_is_read_without_regard_to_case_or_spacing(string kind)
    {
        var (postId, _) = await PublishedPostAsync();

        var state = await _reactions.ToggleAsync(postId, Visitor(), kind);

        (state.ClapCount + state.InsightfulCount).Should().Be(1);
    }

    [Theory]
    [InlineData("love")]
    [InlineData("0")]
    [InlineData("1")]
    [InlineData("")]
    [InlineData(null)]
    public async Task Only_the_two_named_reactions_exist(string? kind)
    {
        var (postId, _) = await PublishedPostAsync();

        var act = () => _reactions.ToggleAsync(postId, Visitor(), kind);

        await act.Should().ThrowAsync<DomainException>().WithMessage("*clap or insightful*");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("someone")]
    public async Task A_reaction_from_a_browser_that_cannot_be_identified_is_refused_and_changes_nothing(string? visitorId)
    {
        var (postId, _) = await PublishedPostAsync();

        var act = () => _reactions.ToggleAsync(postId, visitorId, "clap");

        await act.Should().ThrowAsync<DomainException>();
        (await StoredAsync(postId)).ClapCount.Should().Be(0);
    }

    [Fact]
    public async Task A_post_that_is_not_published_cannot_be_reacted_to_or_inspected()
    {
        var (postId, author) = await PublishedPostAsync();
        await _reactions.ToggleAsync(postId, Visitor(), "clap");
        await _posts.SetStatusAsync(postId, "Inactive", author.Id);

        (await Record.ExceptionAsync(() => _reactions.ToggleAsync(postId, Visitor(), "clap"))).Should().BeOfType<NotFoundException>();
        (await Record.ExceptionAsync(() => _reactions.GetAsync(postId, Visitor()))).Should().BeOfType<NotFoundException>();
        (await Record.ExceptionAsync(() => _reactions.GetAsync(Guid.NewGuid(), Visitor()))).Should().BeOfType<NotFoundException>();
        (await StoredAsync(postId)).ClapCount.Should().Be(1, "reactions given while it was live are kept for when it returns");
    }

    [Fact]
    public async Task Only_a_hash_of_the_visitor_is_stored()
    {
        var (postId, _) = await PublishedPostAsync();
        var visitor = Visitor();

        await _reactions.ToggleAsync(postId, visitor, "clap");

        var stored = _fixture.Db.Reactions.Single();
        stored.VisitorKey.Should().Be(VisitorKey.From(visitor)).And.NotContain(visitor);
    }

    [Fact]
    public async Task Deleting_a_post_removes_its_reactions_and_view_records()
    {
        var (postId, author) = await PublishedPostAsync();
        var visitor = Visitor();
        await _reactions.ToggleAsync(postId, visitor, "clap");
        await _reactions.RecordViewAsync(postId, visitor, Browser, null);

        await _posts.DeleteAsync(postId, author.Id);

        _fixture.Db.Reactions.Should().BeEmpty();
        _fixture.Db.PostViews.Should().BeEmpty();
    }

    // ---- Views --------------------------------------------------------------------------------

    [Fact]
    public async Task A_visitor_is_counted_once_however_often_they_reload()
    {
        var (postId, _) = await PublishedPostAsync();
        var visitor = Visitor();

        var first = await _reactions.RecordViewAsync(postId, visitor, Browser, null);
        var again = await _reactions.RecordViewAsync(postId, visitor, Browser, null);
        var andAgain = await _reactions.RecordViewAsync(postId, visitor, Browser, null);

        (first, again, andAgain).Should().Be((true, false, false));
        (await StoredAsync(postId)).ViewCount.Should().Be(1);
    }

    [Fact]
    public async Task Different_visitors_are_each_counted()
    {
        var (postId, _) = await PublishedPostAsync();

        for (var i = 0; i < 4; i++) await _reactions.RecordViewAsync(postId, Visitor(), Browser, null);

        (await StoredAsync(postId)).ViewCount.Should().Be(4);
    }

    [Fact]
    public async Task The_same_visitor_coming_back_another_day_counts_again()
    {
        var (postId, _) = await PublishedPostAsync();
        var visitor = Visitor();
        await _reactions.RecordViewAsync(postId, visitor, Browser, null);

        _clock.Advance(TimeSpan.FromHours(3)); // 22:00 + 3h crosses midnight UTC
        var nextDay = await _reactions.RecordViewAsync(postId, visitor, Browser, null);

        nextDay.Should().BeTrue();
        (await StoredAsync(postId)).ViewCount.Should().Be(2);
    }

    [Fact]
    public async Task Reading_two_posts_counts_one_view_on_each()
    {
        var (first, _) = await PublishedPostAsync("ada");
        var (second, _) = await PublishedPostAsync("bob");
        var visitor = Visitor();

        await _reactions.RecordViewAsync(first, visitor, Browser, null);
        await _reactions.RecordViewAsync(second, visitor, Browser, null);

        (await StoredAsync(first)).ViewCount.Should().Be(1);
        (await StoredAsync(second)).ViewCount.Should().Be(1);
    }

    [Theory]
    [InlineData("Mozilla/5.0 (compatible; Googlebot/2.1; +http://www.google.com/bot.html)")]
    [InlineData("facebookexternalhit/1.1")]
    [InlineData("curl/8.4.0")]
    [InlineData("")]
    [InlineData(null)]
    public async Task Crawlers_and_link_previews_are_not_counted(string? userAgent)
    {
        var (postId, _) = await PublishedPostAsync();

        var counted = await _reactions.RecordViewAsync(postId, Visitor(), userAgent, null);

        counted.Should().BeFalse();
        (await StoredAsync(postId)).ViewCount.Should().Be(0);
    }

    [Fact]
    public async Task The_writer_looking_at_their_own_post_is_not_counted()
    {
        var (postId, author) = await PublishedPostAsync();

        var counted = await _reactions.RecordViewAsync(postId, Visitor(), Browser, author.Id);

        counted.Should().BeFalse();
        (await StoredAsync(postId)).ViewCount.Should().Be(0);
    }

    [Fact]
    public async Task A_view_with_no_usable_visitor_id_is_not_counted()
    {
        var (postId, _) = await PublishedPostAsync();

        (await _reactions.RecordViewAsync(postId, null, Browser, null)).Should().BeFalse();
        (await _reactions.RecordViewAsync(postId, "garbage", Browser, null)).Should().BeFalse();
        (await StoredAsync(postId)).ViewCount.Should().Be(0);
    }

    [Fact]
    public async Task A_post_that_is_not_published_or_does_not_exist_is_quietly_not_counted()
    {
        var (postId, author) = await PublishedPostAsync();
        await _posts.SetStatusAsync(postId, "Draft", author.Id);

        (await _reactions.RecordViewAsync(postId, Visitor(), Browser, null)).Should().BeFalse();
        (await _reactions.RecordViewAsync(Guid.NewGuid(), Visitor(), Browser, null)).Should().BeFalse();
    }

    [Fact]
    public async Task Records_of_who_read_what_are_kept_for_90_days_and_then_discarded()
    {
        var (postId, _) = await PublishedPostAsync();
        await _reactions.RecordViewAsync(postId, Visitor(), Browser, null);
        await _reactions.RecordViewAsync(postId, Visitor(), Browser, null);

        _clock.Advance(TimeSpan.FromDays(ReactionService.KeepViewsDays));
        await _reactions.RecordViewAsync(postId, Visitor(), Browser, null);
        _fixture.Db.ChangeTracker.Clear();
        _fixture.Db.PostViews.Should().HaveCount(3, "kept long enough to tell a returning reader from a new one");

        _clock.Advance(TimeSpan.FromDays(1));
        await _reactions.RecordViewAsync(postId, Visitor(), Browser, null);

        _fixture.Db.ChangeTracker.Clear();
        _fixture.Db.PostViews.Should().HaveCount(2, "records older than 90 days are gone");
        (await StoredAsync(postId)).ViewCount.Should().Be(4, "the count itself is kept");
    }

    [Fact]
    public async Task Fetching_a_post_is_not_a_view()
    {
        var (postId, _) = await PublishedPostAsync();
        var slug = (await StoredAsync(postId)).Slug!;

        for (var i = 0; i < 5; i++) await _posts.GetBySlugAsync(slug, null);

        (await StoredAsync(postId)).ViewCount.Should().Be(0);
    }

    public void Dispose() => _fixture.Dispose();
}
