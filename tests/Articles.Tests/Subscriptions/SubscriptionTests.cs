using System.Net;
using System.Text.Json;
using FluentAssertions;
using Articles.Application.Admin;
using Articles.Application.Posts;
using Articles.Application.Posts.Dtos;
using Articles.Application.Subscriptions;
using Articles.Domain.Entities;
using Articles.Domain.Exceptions;
using Articles.Infrastructure.Email;
using Articles.Infrastructure.Persistence;
using Articles.Infrastructure.Persistence.Repositories;
using Articles.Infrastructure.Security;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace Articles.Tests.Subscriptions;

/// <summary>Stands in for the mail service: remembers what it was asked to send and can be told to refuse.</summary>
public sealed class FakeEmailSender : IEmailSender
{
    public bool IsConfigured { get; set; } = true;
    public int DailyLimit { get; set; } = 300;
    public string SiteUrl { get; set; } = "https://articles.example";

    /// <summary>Addresses the service refuses, as it would once the day's allowance is used up.</summary>
    public Func<EmailMessage, bool> Accepts { get; set; } = _ => true;

    public List<EmailMessage> Sent { get; } = [];

    public Task<bool> SendAsync(EmailMessage message, CancellationToken ct = default)
    {
        if (!Accepts(message)) return Task.FromResult(false);
        Sent.Add(message);
        return Task.FromResult(true);
    }

    /// <summary>The token inside the link of the most recent email of that kind to that address.</summary>
    public string TokenFor(string to, string marker = "token=")
    {
        var text = Sent.Last(m => m.To == to).Text;
        var start = text.IndexOf(marker, StringComparison.Ordinal) + marker.Length;
        var end = text.IndexOfAny(['\r', '\n', ' '], start);
        return Uri.UnescapeDataString(end < 0 ? text[start..] : text[start..end]);
    }
}

public static class TestTokens
{
    public static SubscriberTokens Create(string key = "a-test-signing-key-that-is-long-enough-123456") =>
        new(Options.Create(new JwtOptions { Key = key }));
}

public class SubscriberTokensTests
{
    private readonly SubscriberTokens _tokens = TestTokens.Create();

    [Fact]
    public void A_token_reads_back_as_the_subscriber_it_was_made_for()
    {
        var id = Guid.NewGuid();

        _tokens.Read(_tokens.Create(id, TokenPurpose.Confirm), TokenPurpose.Confirm).Should().Be(id);
    }

    [Fact]
    public void A_confirm_link_cannot_be_used_to_unsubscribe_and_the_reverse()
    {
        var id = Guid.NewGuid();

        _tokens.Read(_tokens.Create(id, TokenPurpose.Confirm), TokenPurpose.Unsubscribe).Should().BeNull();
        _tokens.Read(_tokens.Create(id, TokenPurpose.Unsubscribe), TokenPurpose.Confirm).Should().BeNull();
    }

    [Fact]
    public void A_token_for_one_subscriber_cannot_be_edited_to_name_another()
    {
        var mine = _tokens.Create(Guid.NewGuid(), TokenPurpose.Unsubscribe);
        var theirs = _tokens.Create(Guid.NewGuid(), TokenPurpose.Unsubscribe);

        var forged = $"{theirs.Split('.')[0]}.{mine.Split('.')[1]}";

        _tokens.Read(forged, TokenPurpose.Unsubscribe).Should().BeNull();
    }

    [Fact]
    public void A_token_signed_with_a_different_key_is_refused()
    {
        var id = Guid.NewGuid();
        var other = TestTokens.Create("a-completely-different-signing-key-0987654321");

        _tokens.Read(other.Create(id, TokenPurpose.Confirm), TokenPurpose.Confirm).Should().BeNull();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("no-dot-in-this")]
    [InlineData("a.b.c")]
    [InlineData("!!!.???")]
    [InlineData("AAAA.BBBB")]
    public void Anything_that_is_not_a_token_is_refused_without_error(string? token) =>
        _tokens.Read(token, TokenPurpose.Confirm).Should().BeNull();

    [Fact]
    public void A_token_is_safe_to_put_in_a_link_and_survives_stray_spaces()
    {
        var id = Guid.NewGuid();
        var token = _tokens.Create(id, TokenPurpose.Confirm);

        token.Should().MatchRegex("^[A-Za-z0-9_-]+\\.[A-Za-z0-9_-]+$");
        _tokens.Read($"  {token} ", TokenPurpose.Confirm).Should().Be(id);
    }

    [Fact]
    public void An_absurdly_long_value_is_refused_before_any_work_is_done() =>
        _tokens.Read(new string('A', 5000) + "." + new string('B', 5000), TokenPurpose.Confirm).Should().BeNull();
}

public class EmailTemplatesTests
{
    private const string Site = "https://articles.example";

    [Fact]
    public void No_email_still_calls_the_site_Articles_and_both_say_who_writes_it()
    {
        var confirmation = EmailTemplates.Confirmation("reader@example.com", $"{Site}/subscribe/confirm?token=abc.def", Site);
        var announcement = EmailTemplates.NewPost("reader@example.com", "A title", "An excerpt.", $"{Site}/read/a-title", $"{Site}/unsubscribe?token=abc", Site);

        foreach (var message in new[] { confirmation, announcement })
        {
            (message.Subject + message.Text + message.Html).Should().NotContain("Ink" + "well");
            (message.Text + message.Html).Should().Contain("Articles by Ahmad Nadeem");
        }

        confirmation.Subject.Should().Be("Confirm your subscription to Articles");
        announcement.Subject.Should().Be("New article: A title");
    }

    [Fact]
    public void The_confirmation_carries_its_link_in_both_forms_and_says_what_to_do_if_it_was_not_you()
    {
        var message = EmailTemplates.Confirmation("reader@example.com", $"{Site}/subscribe/confirm?token=abc.def", Site);

        message.To.Should().Be("reader@example.com");
        message.Text.Should().Contain($"{Site}/subscribe/confirm?token=abc.def").And.Contain("ignore this email");
        message.Html.Should().Contain("subscribe/confirm?token=abc.def").And.Contain("ignore this email");
        message.UnsubscribeUrl.Should().BeNull("nobody is subscribed yet");
    }

    [Fact]
    public void An_announcement_has_the_title_the_link_and_a_way_out()
    {
        var message = EmailTemplates.NewPost("reader@example.com", "Today for Tomorrow", "A short excerpt.", $"{Site}/read/today-for-tomorrow", $"{Site}/unsubscribe?token=abc.def", Site);

        message.Subject.Should().Be("New article: Today for Tomorrow");
        message.Text.Should().Contain("A short excerpt.").And.Contain($"{Site}/read/today-for-tomorrow").And.Contain($"Unsubscribe: {Site}/unsubscribe?token=abc.def");
        message.Html.Should().Contain("Today for Tomorrow").And.Contain("/read/today-for-tomorrow").And.Contain("Unsubscribe");
        message.UnsubscribeUrl.Should().Be($"{Site}/unsubscribe?token=abc.def");
    }

    [Fact]
    public void A_title_or_excerpt_cannot_inject_markup_into_the_email()
    {
        var message = EmailTemplates.NewPost("reader@example.com", "<script>alert(1)</script>", "<img src=x onerror=alert(2)> & more", $"{Site}/read/x", $"{Site}/unsubscribe?token=t", Site);

        message.Html.Should().NotContain("<script>").And.NotContain("<img src=x");
        message.Html.Should().Contain("&lt;script&gt;").And.Contain("&amp; more");
    }

    [Fact]
    public void A_line_break_in_a_title_cannot_add_a_header_to_the_email()
    {
        var message = EmailTemplates.NewPost("reader@example.com", "A title\r\nBcc: victim@example.com", "Excerpt.", $"{Site}/read/x", $"{Site}/unsubscribe?token=t", Site);

        message.Subject.Should().NotContain("\r").And.NotContain("\n");
        message.Subject.Should().Be("New article: A title Bcc: victim@example.com");
    }
}

public class BrevoEmailSenderTests
{
    private sealed class StubHandler(HttpStatusCode status, string body = "{}") : HttpMessageHandler
    {
        public HttpRequestMessage? Request { get; private set; }
        public string? Body { get; private set; }
        public bool Throw { get; set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            if (Throw) throw new HttpRequestException("network down");
            Request = request;
            Body = request.Content is null ? null : await request.Content.ReadAsStringAsync(ct);
            return new HttpResponseMessage(status) { Content = new StringContent(body) };
        }
    }

    private static (BrevoEmailSender Sender, StubHandler Handler) Create(HttpStatusCode status = HttpStatusCode.Created, Action<EmailOptions>? configure = null)
    {
        var options = new EmailOptions { ApiKey = "test-key", FromAddress = "hello@articles.example", SiteUrl = "https://articles.example/" };
        configure?.Invoke(options);
        var handler = new StubHandler(status, """{"code":"unauthorized","message":"Key not found"}""");
        return (new BrevoEmailSender(new HttpClient(handler), Options.Create(options), NullLogger<BrevoEmailSender>.Instance), handler);
    }

    private static readonly EmailMessage Message = new("reader@example.com", "A subject", "Plain words", "<p>Rich words</p>", "https://articles.example/unsubscribe?token=t");

    [Fact]
    public async Task It_sends_the_message_to_brevo_with_the_key_in_a_header_not_in_the_body()
    {
        var (sender, handler) = Create();

        (await sender.SendAsync(Message)).Should().BeTrue();

        handler.Request!.Method.Should().Be(HttpMethod.Post);
        handler.Request.RequestUri!.ToString().Should().Be("https://api.brevo.com/v3/smtp/email");
        handler.Request.Headers.GetValues("api-key").Should().ContainSingle("test-key");
        handler.Body.Should().NotContain("test-key");

        var body = JsonDocument.Parse(handler.Body!).RootElement;
        body.GetProperty("sender").GetProperty("email").GetString().Should().Be("hello@articles.example");
        body.GetProperty("to")[0].GetProperty("email").GetString().Should().Be("reader@example.com");
        body.GetProperty("subject").GetString().Should().Be("A subject");
        body.GetProperty("textContent").GetString().Should().Be("Plain words");
        body.GetProperty("htmlContent").GetString().Should().Be("<p>Rich words</p>");
        body.GetProperty("headers").GetProperty("List-Unsubscribe").GetString().Should().Be("<https://articles.example/unsubscribe?token=t>");
    }

    [Fact]
    public async Task A_message_with_no_unsubscribe_link_sends_no_extra_headers()
    {
        var (sender, handler) = Create();

        await sender.SendAsync(Message with { UnsubscribeUrl = null });

        JsonDocument.Parse(handler.Body!).RootElement.TryGetProperty("headers", out _).Should().BeFalse();
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized)]
    [InlineData(HttpStatusCode.TooManyRequests)]
    [InlineData(HttpStatusCode.BadRequest)]
    [InlineData(HttpStatusCode.InternalServerError)]
    public async Task A_refusal_is_reported_as_not_sent_rather_than_thrown(HttpStatusCode status)
    {
        var (sender, _) = Create(status);

        (await sender.SendAsync(Message)).Should().BeFalse();
    }

    [Fact]
    public async Task An_unreachable_service_is_reported_as_not_sent_rather_than_thrown()
    {
        var (sender, handler) = Create();
        handler.Throw = true;

        (await sender.SendAsync(Message)).Should().BeFalse();
    }

    [Theory]
    [InlineData("", "hello@articles.example", "https://articles.example")]
    [InlineData("key", "", "https://articles.example")]
    [InlineData("key", "hello@articles.example", "not a web address")]
    public async Task Without_a_key_a_from_address_and_a_site_address_it_is_switched_off_and_calls_nothing(string key, string from, string site)
    {
        var (sender, handler) = Create(configure: o => { o.ApiKey = key; o.FromAddress = from; o.SiteUrl = site; });

        sender.IsConfigured.Should().BeFalse();
        (await sender.SendAsync(Message)).Should().BeFalse();
        handler.Request.Should().BeNull();
    }

    [Fact]
    public void The_site_address_never_ends_in_a_slash_so_links_are_not_doubled()
    {
        var (sender, _) = Create();

        sender.SiteUrl.Should().Be("https://articles.example");
    }
}

public class SubscriptionServiceTests : IDisposable
{
    private const string Owner = "owner@example.com";
    private const string Reader = "reader@example.com";

    private readonly TestDatabase _fixture = new();
    private readonly ManualTimeProvider _clock = new(new DateTimeOffset(2026, 6, 15, 10, 0, 0, TimeSpan.Zero));
    private readonly FakeEmailSender _email = new();
    private readonly SubscriberTokens _tokens = TestTokens.Create();
    private readonly SubscriberRepository _subscribers;
    private readonly ActivityLog _log;
    private readonly SubscriptionService _service;
    private readonly PostService _posts;

    public SubscriptionServiceTests()
    {
        _subscribers = new SubscriberRepository(_fixture.Db);
        _log = new ActivityLog(_fixture.Db, NullLogger<ActivityLog>.Instance, _clock);
        _service = new SubscriptionService(_subscribers, _fixture.Posts, _email, _tokens, _fixture.Db, NullLogger<SubscriptionService>.Instance, _log, _clock);
        _posts = new PostService(_fixture.Posts, _fixture.Tags, _fixture.Engagement, _fixture.Db);
    }

    private Task SubscribeAsync(string email) => _service.SubscribeAsync(new SubscribeRequest(email));

    private async Task ConfirmedAsync(string email)
    {
        await SubscribeAsync(email);
        await _service.ConfirmAsync(_email.TokenFor(email));
    }

    private async Task<Subscriber> StoredAsync(string email)
    {
        _fixture.Db.ChangeTracker.Clear();
        return (await _subscribers.GetByEmailAsync(email))!;
    }

    private async Task<(Guid Id, string Slug)> PublishedPostAsync(string title = "Today for Tomorrow")
    {
        var author = await _fixture.AddUserAsync($"author{Guid.NewGuid():N}"[..12]);
        var draft = await _posts.CreateDraftAsync(new CreatePostRequest(title, "A subtitle for the email.", TestDatabase.Document("Body."), null, []), author.Id);
        var published = await _posts.PublishAsync(draft.Id, author.Id);
        return (draft.Id, published.Slug!);
    }

    // ---- Subscribing --------------------------------------------------------------------------

    [Fact]
    public async Task Asking_to_subscribe_sends_one_confirmation_and_subscribes_nobody_yet()
    {
        await SubscribeAsync("  Reader@Example.COM ");

        _email.Sent.Should().ContainSingle().Which.To.Should().Be(Reader);
        (await StoredAsync(Reader)).Status.Should().Be(SubscriberStatus.Pending);
        (await _service.GetSummaryAsync()).Should().BeEquivalentTo(new { Confirmed = 0, Pending = 1 });
    }

    [Fact]
    public async Task Clicking_the_link_in_the_email_confirms_the_subscription()
    {
        await SubscribeAsync(Reader);

        await _service.ConfirmAsync(_email.TokenFor(Reader));

        var stored = await StoredAsync(Reader);
        stored.Status.Should().Be(SubscriberStatus.Confirmed);
        stored.ConfirmedAt.Should().Be(_clock.GetUtcNow());
    }

    [Fact]
    public async Task Confirming_twice_is_harmless()
    {
        await SubscribeAsync(Reader);
        var token = _email.TokenFor(Reader);
        await _service.ConfirmAsync(token);
        var first = (await StoredAsync(Reader)).ConfirmedAt;

        _clock.Advance(TimeSpan.FromDays(3));
        await _service.ConfirmAsync(token);

        (await StoredAsync(Reader)).ConfirmedAt.Should().Be(first);
    }

    [Fact]
    public async Task Asking_again_for_an_address_that_is_waiting_does_not_send_a_second_email_the_same_day()
    {
        await SubscribeAsync(Reader);
        await SubscribeAsync(Reader);
        await SubscribeAsync(Reader.ToUpperInvariant());

        _email.Sent.Should().ContainSingle("typing a stranger's address must not be a way to pester them");
        _fixture.Db.Subscribers.Should().ContainSingle();
    }

    [Fact]
    public async Task A_day_later_the_confirmation_can_be_sent_again()
    {
        await SubscribeAsync(Reader);

        _clock.Advance(SubscriptionService.ConfirmationCooldown + TimeSpan.FromMinutes(1));
        await SubscribeAsync(Reader);

        _email.Sent.Should().HaveCount(2);
    }

    [Fact]
    public async Task Asking_for_an_address_that_is_already_subscribed_does_nothing_and_says_nothing()
    {
        await ConfirmedAsync(Reader);
        _email.Sent.Clear();

        var act = () => SubscribeAsync(Reader);

        await act.Should().NotThrowAsync();
        _email.Sent.Should().BeEmpty();
        (await StoredAsync(Reader)).Status.Should().Be(SubscriberStatus.Confirmed);
    }

    [Fact]
    public async Task If_the_confirmation_cannot_be_sent_it_is_tried_again_on_the_next_request()
    {
        _email.Accepts = _ => false;
        await SubscribeAsync(Reader);

        _email.Accepts = _ => true;
        await SubscribeAsync(Reader);

        _email.Sent.Should().ContainSingle();
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("not-an-address")]
    [InlineData("two@@example.com")]
    [InlineData("no-domain@")]
    [InlineData("@no-name.com")]
    [InlineData("nodot@localhost")]
    [InlineData("trailing@example.")]
    [InlineData("spaces in@example.com")]
    [InlineData("Display Name <reader@example.com>")]
    [InlineData("reader@example.com\r\nBcc: victim@example.com")]
    public async Task Something_that_is_not_one_plain_address_is_refused_and_nothing_is_sent(string email)
    {
        var failure = await Record.ExceptionAsync(() => SubscribeAsync(email));

        failure.Should().BeOfType<DomainException>();
        _email.Sent.Should().BeEmpty();
        _fixture.Db.Subscribers.Should().BeEmpty();
    }

    [Fact]
    public async Task While_email_is_not_set_up_subscribing_is_refused_outright()
    {
        _email.IsConfigured = false;

        var failure = await Record.ExceptionAsync(() => SubscribeAsync(Reader));

        _service.IsAvailable.Should().BeFalse();
        failure.Should().BeOfType<DomainException>().Which.Message.Should().Contain("not available");
        _fixture.Db.Subscribers.Should().BeEmpty();
    }

    [Theory]
    [InlineData("")]
    [InlineData("garbage")]
    [InlineData("AAAA.BBBB")]
    public async Task A_broken_link_confirms_nothing(string token)
    {
        await SubscribeAsync(Reader);

        var failure = await Record.ExceptionAsync(() => _service.ConfirmAsync(token));

        failure.Should().BeOfType<DomainException>().Which.Message.Should().Contain("not valid");
        (await StoredAsync(Reader)).Status.Should().Be(SubscriberStatus.Pending);
    }

    [Fact]
    public async Task A_valid_looking_link_for_a_subscriber_who_has_been_removed_confirms_nothing()
    {
        var token = _tokens.Create(Guid.NewGuid(), TokenPurpose.Confirm);

        (await Record.ExceptionAsync(() => _service.ConfirmAsync(token))).Should().BeOfType<DomainException>();
    }

    // ---- Unsubscribing ------------------------------------------------------------------------

    [Fact]
    public async Task The_link_in_an_announcement_unsubscribes_and_no_more_announcements_arrive()
    {
        await ConfirmedAsync(Reader);
        var (first, _) = await PublishedPostAsync("First");
        await _service.NotifyAsync(first, Owner);

        await _service.UnsubscribeAsync(_email.TokenFor(Reader, "unsubscribe?token="));

        (await StoredAsync(Reader)).Status.Should().Be(SubscriberStatus.Unsubscribed);
        var (second, _) = await PublishedPostAsync("Second");
        (await Record.ExceptionAsync(() => _service.NotifyAsync(second, Owner))).Should().BeOfType<DomainException>();
        _email.Sent.Count(m => m.Subject.Contains("Second")).Should().Be(0);
    }

    [Fact]
    public async Task Unsubscribing_twice_is_harmless_and_an_old_confirm_link_cannot_undo_it()
    {
        await SubscribeAsync(Reader);
        var confirm = _email.TokenFor(Reader);
        await _service.ConfirmAsync(confirm);
        var id = (await StoredAsync(Reader)).Id;
        var unsubscribe = _tokens.Create(id, TokenPurpose.Unsubscribe);

        await _service.UnsubscribeAsync(unsubscribe);
        await _service.UnsubscribeAsync(unsubscribe);
        var reuse = await Record.ExceptionAsync(() => _service.ConfirmAsync(confirm));

        reuse.Should().BeOfType<DomainException>();
        (await StoredAsync(Reader)).Status.Should().Be(SubscriberStatus.Unsubscribed);
    }

    [Fact]
    public async Task Someone_who_unsubscribed_can_subscribe_again_but_must_confirm_again()
    {
        await ConfirmedAsync(Reader);
        var id = (await StoredAsync(Reader)).Id;
        await _service.UnsubscribeAsync(_tokens.Create(id, TokenPurpose.Unsubscribe));
        _clock.Advance(TimeSpan.FromDays(2));

        await SubscribeAsync(Reader);

        (await StoredAsync(Reader)).Status.Should().Be(SubscriberStatus.Pending);
        await _service.ConfirmAsync(_email.TokenFor(Reader));
        (await StoredAsync(Reader)).Status.Should().Be(SubscriberStatus.Confirmed);
    }

    // ---- Announcing a post --------------------------------------------------------------------

    [Fact]
    public async Task Notifying_emails_every_confirmed_subscriber_once_with_a_link_to_the_post()
    {
        await ConfirmedAsync("a@example.com");
        await ConfirmedAsync("b@example.com");
        await SubscribeAsync("pending@example.com");
        _email.Sent.Clear();
        var (postId, slug) = await PublishedPostAsync();

        var result = await _service.NotifyAsync(postId, Owner);

        result.Should().BeEquivalentTo(new { Sent = 2, Failed = 0, Remaining = 0 });
        result.NotifiedAt.Should().Be(_clock.GetUtcNow());
        _email.Sent.Select(m => m.To).Should().BeEquivalentTo(["a@example.com", "b@example.com"]);
        _email.Sent.Should().OnlyContain(m => m.Subject == "New article: Today for Tomorrow" && m.Text.Contains($"https://articles.example/read/{slug}") && m.Text.Contains("A subtitle for the email."));
    }

    [Fact]
    public async Task Every_subscriber_gets_their_own_unsubscribe_link()
    {
        await ConfirmedAsync("a@example.com");
        await ConfirmedAsync("b@example.com");
        _email.Sent.Clear();
        var (postId, _) = await PublishedPostAsync();

        await _service.NotifyAsync(postId, Owner);

        var links = _email.Sent.Select(m => m.UnsubscribeUrl).ToList();
        links.Should().OnlyHaveUniqueItems().And.NotContainNulls();
        await _service.UnsubscribeAsync(_email.TokenFor("a@example.com", "unsubscribe?token="));
        (await StoredAsync("a@example.com")).Status.Should().Be(SubscriberStatus.Unsubscribed);
        (await StoredAsync("b@example.com")).Status.Should().Be(SubscriberStatus.Confirmed);
    }

    [Fact]
    public async Task Pressing_notify_a_second_time_tells_nobody_twice()
    {
        await ConfirmedAsync(Reader);
        _email.Sent.Clear();
        var (postId, _) = await PublishedPostAsync();
        await _service.NotifyAsync(postId, Owner);

        var again = await Record.ExceptionAsync(() => _service.NotifyAsync(postId, Owner));

        again.Should().BeOfType<ConflictException>().Which.Message.Should().Contain("already been told");
        _email.Sent.Should().ContainSingle();
    }

    [Fact]
    public async Task Someone_who_subscribes_after_an_announcement_can_still_be_told_without_repeating_it_to_the_others()
    {
        await ConfirmedAsync("early@example.com");
        var (postId, _) = await PublishedPostAsync();
        await _service.NotifyAsync(postId, Owner);
        await ConfirmedAsync("late@example.com");
        _email.Sent.Clear();

        (await _service.CountWaitingAsync(postId)).Should().Be(1);
        var result = await _service.NotifyAsync(postId, Owner);

        result.Sent.Should().Be(1);
        _email.Sent.Should().ContainSingle().Which.To.Should().Be("late@example.com");
    }

    [Fact]
    public async Task If_the_mail_service_refuses_some_the_rest_can_be_sent_later()
    {
        foreach (var name in new[] { "a", "b", "c", "d" }) await ConfirmedAsync($"{name}@example.com");
        _email.Sent.Clear();
        var (postId, _) = await PublishedPostAsync();

        _email.Accepts = m => m.To is "a@example.com" or "b@example.com";
        var first = await _service.NotifyAsync(postId, Owner);

        first.Should().BeEquivalentTo(new { Sent = 2, Failed = 2, Remaining = 2 });

        _email.Accepts = _ => true;
        var second = await _service.NotifyAsync(postId, Owner);

        second.Should().BeEquivalentTo(new { Sent = 2, Failed = 0, Remaining = 0 });
        _email.Sent.Select(m => m.To).Should().BeEquivalentTo(["a@example.com", "b@example.com", "c@example.com", "d@example.com"]);
    }

    [Fact]
    public async Task If_nothing_at_all_gets_through_it_stops_early_and_the_post_is_not_marked_as_announced()
    {
        for (var i = 0; i < 10; i++) await ConfirmedAsync($"reader{i}@example.com");
        var attempts = 0;
        _email.Accepts = _ => { attempts++; return false; };
        var (postId, _) = await PublishedPostAsync();

        var result = await _service.NotifyAsync(postId, Owner);

        result.Should().BeEquivalentTo(new { Sent = 0, Failed = 3, Remaining = 10 });
        result.NotifiedAt.Should().BeNull();
        attempts.Should().Be(3, "three refusals in a row means the service is not accepting mail");
    }

    [Fact]
    public async Task One_run_never_sends_more_than_the_mail_services_daily_allowance()
    {
        _email.DailyLimit = 3;
        for (var i = 0; i < 5; i++) await ConfirmedAsync($"reader{i}@example.com");
        _email.Sent.Clear();
        var (postId, _) = await PublishedPostAsync();

        var result = await _service.NotifyAsync(postId, Owner);

        result.Should().BeEquivalentTo(new { Sent = 3, Remaining = 2 });
        _email.Sent.Should().HaveCount(3);
    }

    [Fact]
    public async Task Only_a_published_post_can_be_announced()
    {
        await ConfirmedAsync(Reader);
        _email.Sent.Clear();
        var author = await _fixture.AddUserAsync();
        var draft = await _posts.CreateDraftAsync(new CreatePostRequest("Not ready", null, TestDatabase.Document("Body."), null, []), author.Id);
        var (live, _) = await PublishedPostAsync("Was live");
        var liveAuthor = (await _fixture.Posts.GetByIdAsync(live))!.AuthorId;
        await _posts.SetStatusAsync(live, "Inactive", liveAuthor);

        (await Record.ExceptionAsync(() => _service.NotifyAsync(draft.Id, Owner))).Should().BeOfType<DomainException>();
        (await Record.ExceptionAsync(() => _service.NotifyAsync(live, Owner))).Should().BeOfType<DomainException>();
        (await Record.ExceptionAsync(() => _service.NotifyAsync(Guid.NewGuid(), Owner))).Should().BeOfType<NotFoundException>();
        _email.Sent.Should().BeEmpty();
    }

    [Fact]
    public async Task With_no_confirmed_subscribers_there_is_nobody_to_tell()
    {
        await SubscribeAsync("pending@example.com");
        var (postId, _) = await PublishedPostAsync();

        var failure = await Record.ExceptionAsync(() => _service.NotifyAsync(postId, Owner));

        failure.Should().BeOfType<DomainException>().Which.Message.Should().Contain("no confirmed subscribers");
    }

    [Fact]
    public async Task An_announcement_is_recorded_in_the_history_without_any_addresses()
    {
        await ConfirmedAsync(Reader);
        var (postId, _) = await PublishedPostAsync();

        await _service.NotifyAsync(postId, Owner);

        var entry = (await _log.GetPageAsync(1, 10)).Items.Single();
        entry.Action.Should().Be(Activity.NotifiedSubscribers);
        entry.Subject.Should().Be("Today for Tomorrow: 1 sent, 0 still to send").And.NotContain("@");
    }

    // ---- The owner's list ---------------------------------------------------------------------

    [Fact]
    public async Task The_owner_sees_counts_by_state_and_can_remove_an_address()
    {
        await ConfirmedAsync("a@example.com");
        await SubscribeAsync("b@example.com");
        var id = (await StoredAsync("a@example.com")).Id;

        var summary = await _service.GetSummaryAsync();
        var page = await _service.GetPageAsync(1, 10);
        await _service.RemoveAsync(id, Owner);

        summary.Should().BeEquivalentTo(new SubscribersSummaryDto(true, 1, 1, 0, 300));
        page.Items.Select(s => (s.Email, s.Status)).Should().BeEquivalentTo([("a@example.com", "Confirmed"), ("b@example.com", "Pending")]);
        _fixture.Db.ChangeTracker.Clear();
        (await _subscribers.GetByEmailAsync("a@example.com")).Should().BeNull();
        (await _log.GetPageAsync(1, 10)).Items.Should().ContainSingle(e => e.Action == Activity.RemovedSubscriber && !e.Subject.Contains('@'));
    }

    [Fact]
    public async Task Removing_an_address_that_is_not_there_is_not_found()
    {
        (await Record.ExceptionAsync(() => _service.RemoveAsync(Guid.NewGuid(), Owner))).Should().BeOfType<NotFoundException>();
    }

    public void Dispose() => _fixture.Dispose();
}
