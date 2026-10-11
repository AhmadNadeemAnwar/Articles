using System.Net;
using System.Net.Mail;
using FluentValidation;
using Articles.Application.Admin;
using Articles.Application.Common;
using Articles.Domain.Common;
using Articles.Domain.Entities;
using Articles.Domain.Enums;
using Articles.Domain.Exceptions;
using Articles.Domain.Interfaces;
using Microsoft.Extensions.Logging;

namespace Articles.Application.Subscriptions;

public sealed record SubscribeRequest(string Email);

public sealed record TokenRequest(string Token);

public sealed record SubscribersSummaryDto(bool EmailConfigured, int Confirmed, int Pending, int Unsubscribed, int DailyLimit);

public sealed record SubscriberDto(Guid Id, string Email, string Status, DateTimeOffset CreatedAt, DateTimeOffset? ConfirmedAt);

/// <summary>What happened when subscribers were told about a post, and whether anyone is still waiting to hear.</summary>
public sealed record NotifyResultDto(int Sent, int Failed, int Remaining, DateTimeOffset? NotifiedAt);

public sealed class SubscribeRequestValidator : AbstractValidator<SubscribeRequest>
{
    public SubscribeRequestValidator() =>
        RuleFor(x => x.Email).NotEmpty().WithMessage("Enter your email address.").MaximumLength(Subscriber.MaxEmailLength);
}

/// <summary>One outgoing message. The sender adds the "from" address; nothing here is provider-specific.</summary>
public sealed record EmailMessage(string To, string Subject, string Text, string Html, string? UnsubscribeUrl = null);

public interface IEmailSender
{
    /// <summary>False until a mail service key and a "from" address are set. Subscribing is switched off until then.</summary>
    bool IsConfigured { get; }

    /// <summary>The most messages the mail service's free plan allows in a day.</summary>
    int DailyLimit { get; }

    /// <summary>Where the public site lives, for the links inside emails.</summary>
    string SiteUrl { get; }

    /// <summary>Returns false if the service refused or could not be reached; never throws for that.</summary>
    Task<bool> SendAsync(EmailMessage message, CancellationToken ct = default);
}

/// <summary>
/// Links in emails carry a signed token instead of a database lookup key, so a link cannot be
/// forged for someone else's address and nothing extra has to be stored per link.
/// </summary>
public interface ISubscriberTokens
{
    string Create(Guid subscriberId, string purpose);
    Guid? Read(string? token, string purpose);
}

public static class TokenPurpose
{
    public const string Confirm = "confirm";
    public const string Unsubscribe = "unsubscribe";
}

public interface ISubscriptionService
{
    bool IsAvailable { get; }

    /// <summary>Starts a subscription. Answers the same way whatever the address's history, so it reveals nothing about who is subscribed.</summary>
    Task SubscribeAsync(SubscribeRequest request, CancellationToken ct = default);
    Task ConfirmAsync(string token, CancellationToken ct = default);
    Task UnsubscribeAsync(string token, CancellationToken ct = default);

    Task<SubscribersSummaryDto> GetSummaryAsync(CancellationToken ct = default);
    Task<PagedResult<SubscriberDto>> GetPageAsync(int pageNumber, int pageSize, CancellationToken ct = default);
    Task RemoveAsync(Guid id, string admin, CancellationToken ct = default);

    /// <summary>How many confirmed subscribers have not been told about this post yet.</summary>
    Task<int> CountWaitingAsync(Guid postId, CancellationToken ct = default);

    /// <summary>Emails confirmed subscribers about a published post. Safe to run again: nobody is told twice.</summary>
    Task<NotifyResultDto> NotifyAsync(Guid postId, string admin, CancellationToken ct = default);
}

public sealed class SubscriptionService : ISubscriptionService
{
    /// <summary>A confirmation is re-sent to the same address at most this often, however many times it is asked for.</summary>
    public static readonly TimeSpan ConfirmationCooldown = TimeSpan.FromHours(24);

    private readonly ISubscriberRepository _subscribers;
    private readonly IPostRepository _posts;
    private readonly IEmailSender _email;
    private readonly ISubscriberTokens _tokens;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IActivityLog _activity;
    private readonly ILogger<SubscriptionService> _logger;
    private readonly TimeProvider _time;

    public SubscriptionService(
        ISubscriberRepository subscribers,
        IPostRepository posts,
        IEmailSender email,
        ISubscriberTokens tokens,
        IUnitOfWork unitOfWork,
        ILogger<SubscriptionService> logger,
        IActivityLog? activity = null,
        TimeProvider? time = null)
    {
        _subscribers = subscribers;
        _posts = posts;
        _email = email;
        _tokens = tokens;
        _unitOfWork = unitOfWork;
        _logger = logger;
        _activity = activity ?? NoActivityLog.Instance;
        _time = time ?? TimeProvider.System;
    }

    public bool IsAvailable => _email.IsConfigured;

    public async Task SubscribeAsync(SubscribeRequest request, CancellationToken ct = default)
    {
        if (!IsAvailable) throw new DomainException("Subscribing is not available yet.");

        var email = Subscriber.Normalise(request.Email);
        if (!IsPlausibleAddress(email)) throw new DomainException("That does not look like an email address.");

        var now = _time.GetUtcNow();
        var subscriber = await _subscribers.GetByEmailAsync(email, ct);

        // Already confirmed: nothing to do, and nothing to say that would reveal it.
        if (subscriber is { Status: SubscriberStatus.Confirmed }) return;

        if (subscriber is null)
        {
            subscriber = new Subscriber(email);
            await _subscribers.AddAsync(subscriber, ct);
        }
        else
        {
            subscriber.RequestAgain();
        }

        // Anyone can type anyone's address into the form, so an address is sent at most one
        // confirmation a day. That is what keeps this from being a way to pester a stranger.
        var recentlySent = subscriber.ConfirmationSentAt is { } sent && now - sent < ConfirmationCooldown;
        if (!recentlySent)
        {
            var delivered = await _email.SendAsync(EmailTemplates.Confirmation(email, ConfirmUrl(subscriber.Id), _email.SiteUrl), ct);
            if (delivered) subscriber.MarkConfirmationSent(now);
            else _logger.LogWarning("Confirmation email could not be sent");
        }

        await _unitOfWork.SaveChangesAsync(ct);
    }

    public async Task ConfirmAsync(string token, CancellationToken ct = default)
    {
        var subscriber = await FromTokenAsync(token, TokenPurpose.Confirm, ct);

        // Someone who unsubscribed stays unsubscribed until they ask again; an old link must not undo that.
        if (subscriber.Status == SubscriberStatus.Unsubscribed)
            throw new DomainException("This link is no longer valid. Subscribe again to get a new one.");

        subscriber.Confirm(_time.GetUtcNow());
        await _unitOfWork.SaveChangesAsync(ct);
    }

    public async Task UnsubscribeAsync(string token, CancellationToken ct = default)
    {
        var subscriber = await FromTokenAsync(token, TokenPurpose.Unsubscribe, ct);
        subscriber.Unsubscribe();
        await _unitOfWork.SaveChangesAsync(ct);
    }

    public async Task<SubscribersSummaryDto> GetSummaryAsync(CancellationToken ct = default) =>
        new(_email.IsConfigured,
            await _subscribers.CountAsync(SubscriberStatus.Confirmed, ct),
            await _subscribers.CountAsync(SubscriberStatus.Pending, ct),
            await _subscribers.CountAsync(SubscriberStatus.Unsubscribed, ct),
            _email.DailyLimit);

    public async Task<PagedResult<SubscriberDto>> GetPageAsync(int pageNumber, int pageSize, CancellationToken ct = default)
    {
        var page = await _subscribers.GetPageAsync(pageNumber, pageSize, ct);
        return page.Map(s => new SubscriberDto(s.Id, s.Email, s.Status.ToString(), s.CreatedAt, s.ConfirmedAt));
    }

    public async Task RemoveAsync(Guid id, string admin, CancellationToken ct = default)
    {
        var subscriber = await _subscribers.GetAsync(id, ct) ?? throw new NotFoundException(nameof(Subscriber), id);
        _subscribers.Remove(subscriber);
        await _unitOfWork.SaveChangesAsync(ct);

        // The address itself is deliberately left out of the history.
        await _activity.RecordAsync(admin, Activity.RemovedSubscriber, ct: ct);
    }

    public Task<int> CountWaitingAsync(Guid postId, CancellationToken ct = default) =>
        _subscribers.CountUnnotifiedAsync(postId, ct);

    public async Task<NotifyResultDto> NotifyAsync(Guid postId, string admin, CancellationToken ct = default)
    {
        if (!IsAvailable) throw new DomainException("Email is not set up yet, so subscribers cannot be notified.");

        var post = await _posts.GetByIdAsync(postId, ct) ?? throw new NotFoundException(nameof(Post), postId);
        if (post.Status != PostStatus.Published || post.Slug is null)
            throw new DomainException("Only a published post can be announced to subscribers.");

        var waiting = await _subscribers.GetUnnotifiedAsync(postId, _email.DailyLimit, ct);
        if (waiting.Count == 0)
        {
            throw post.NotifiedAt is null
                ? new DomainException("There are no confirmed subscribers yet.")
                : new ConflictException("Every subscriber has already been told about this post.");
        }

        var postUrl = $"{_email.SiteUrl}/read/{post.Slug}";
        int sent = 0, failed = 0;

        foreach (var subscriber in waiting)
        {
            var message = EmailTemplates.NewPost(subscriber.Email, post.Title, post.BuildExcerpt(), postUrl, UnsubscribeUrl(subscriber.Id), _email.SiteUrl);
            if (await _email.SendAsync(message, ct))
            {
                subscriber.MarkNotified(postId);
                sent++;
            }
            else
            {
                failed++;
                // The mail service has most likely hit its daily allowance; hammering it further helps nobody.
                if (failed >= 3 && sent == 0) break;
            }
        }

        if (sent > 0) post.MarkNotified(_time.GetUtcNow());
        await _unitOfWork.SaveChangesAsync(ct);

        var remaining = await _subscribers.CountUnnotifiedAsync(postId, ct);
        await _activity.RecordAsync(admin, Activity.NotifiedSubscribers, $"{post.Title}: {sent} sent, {remaining} still to send", ct);

        return new NotifyResultDto(sent, failed, remaining, post.NotifiedAt);
    }

    private async Task<Subscriber> FromTokenAsync(string token, string purpose, CancellationToken ct)
    {
        const string invalid = "This link is not valid. It may have been copied incompletely.";

        var id = _tokens.Read(token, purpose) ?? throw new DomainException(invalid);
        return await _subscribers.GetAsync(id, ct) ?? throw new DomainException(invalid);
    }

    private string ConfirmUrl(Guid id) => $"{_email.SiteUrl}/subscribe/confirm?token={Uri.EscapeDataString(_tokens.Create(id, TokenPurpose.Confirm))}";
    private string UnsubscribeUrl(Guid id) => $"{_email.SiteUrl}/unsubscribe?token={Uri.EscapeDataString(_tokens.Create(id, TokenPurpose.Unsubscribe))}";

    /// <summary>A light shape check. Whether the address really exists is settled by the confirmation email.</summary>
    internal static bool IsPlausibleAddress(string email)
    {
        if (email.Any(char.IsWhiteSpace) || email.Any(char.IsControl)) return false;
        if (!MailAddress.TryCreate(email, out var parsed) || parsed.Address != email) return false;

        var host = parsed.Host;
        return host.Contains('.') && !host.StartsWith('.') && !host.EndsWith('.') && !host.Contains("..");
    }
}

/// <summary>
/// The words of every email the site sends. Each has a plain-text and an HTML form with the same
/// content, and everything that came from a post or a person is escaped before it goes into HTML.
/// </summary>
public static class EmailTemplates
{
    public static EmailMessage Confirmation(string to, string confirmUrl, string siteUrl)
    {
        const string subject = "Confirm your subscription to Articles";

        var text = $"""
            Someone asked for new articles by Ahmad Nadeem to be sent to this address.

            If that was you, confirm here:
            {confirmUrl}

            If it was not you, ignore this email: you will not be subscribed and will not hear from us again.

            Articles by Ahmad Nadeem - {siteUrl}
            """;

        var html = Layout($"""
            <p>Someone asked for new articles by Ahmad Nadeem to be sent to this address.</p>
            <p>If that was you, confirm here:</p>
            <p>{Button(confirmUrl, "Confirm my subscription")}</p>
            <p style="color:#5c5f66;font-size:14px">If it was not you, ignore this email: you will not be subscribed and will not hear from us again.</p>
            """, siteUrl, null);

        return new EmailMessage(to, subject, text, html);
    }

    public static EmailMessage NewPost(string to, string title, string excerpt, string postUrl, string unsubscribeUrl, string siteUrl)
    {
        var subject = $"New article: {SingleLine(title)}";

        var text = $"""
            {SingleLine(title)}

            {excerpt}

            Read it here:
            {postUrl}

            You are receiving this because you subscribed to Articles by Ahmad Nadeem.
            Unsubscribe: {unsubscribeUrl}
            """;

        var html = Layout($"""
            <h1 style="font-family:Georgia,serif;font-size:24px;line-height:1.25;margin:0 0 12px">{Escape(title)}</h1>
            <p>{Escape(excerpt)}</p>
            <p>{Button(postUrl, "Read the article")}</p>
            """, siteUrl, unsubscribeUrl);

        return new EmailMessage(to, subject, text, html, unsubscribeUrl);
    }

    private static string Layout(string body, string siteUrl, string? unsubscribeUrl)
    {
        var footer = unsubscribeUrl is null
            ? $"""<a href="{Escape(siteUrl)}" style="color:#8a8d93">Articles by Ahmad Nadeem</a>"""
            : $"""You are receiving this because you subscribed to <a href="{Escape(siteUrl)}" style="color:#8a8d93">Articles by Ahmad Nadeem</a>. <a href="{Escape(unsubscribeUrl)}" style="color:#8a8d93">Unsubscribe</a>""";

        return $"""
            <!doctype html>
            <html lang="en"><body style="margin:0;padding:24px;background:#f2f2f1;font-family:system-ui,-apple-system,'Segoe UI',Roboto,sans-serif;font-size:16px;line-height:1.6;color:#1c1e21">
            <div style="max-width:560px;margin:0 auto;background:#ffffff;border-radius:8px;padding:28px">
            {body}
            </div>
            <p style="max-width:560px;margin:16px auto 0;font-size:13px;color:#8a8d93;text-align:center">{footer}</p>
            </body></html>
            """;
    }

    private static string Button(string url, string label) =>
        $"""<a href="{Escape(url)}" style="display:inline-block;background:#185fa5;color:#ffffff;text-decoration:none;border-radius:999px;padding:10px 22px">{Escape(label)}</a>""";

    private static string Escape(string value) => WebUtility.HtmlEncode(value);

    /// <summary>A subject line is one line: a line break in a title must not be able to add headers to the email.</summary>
    private static string SingleLine(string value) =>
        string.Join(' ', value.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
}
