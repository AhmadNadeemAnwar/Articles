using Articles.Domain.Common;
using Articles.Domain.Exceptions;

namespace Articles.Domain.Entities;

public enum SubscriberStatus
{
    /// <summary>Asked to subscribe, but has not yet clicked the link in the confirmation email. Receives nothing else.</summary>
    Pending = 0,
    /// <summary>Confirmed the address is theirs. Receives a note when a new article is announced.</summary>
    Confirmed = 1,
    /// <summary>Asked to stop. Kept (rather than deleted) so the address is never emailed again by mistake.</summary>
    Unsubscribed = 2
}

/// <summary>
/// A reader who asked to hear about new articles. This is the only place the site stores anything
/// that identifies a reader, and it holds nothing but the address and what they asked for.
/// </summary>
public class Subscriber : BaseEntity
{
    public const int MaxEmailLength = 254;

    public string Email { get; private set; } = null!;
    public SubscriberStatus Status { get; private set; } = SubscriberStatus.Pending;
    public DateTimeOffset? ConfirmedAt { get; private set; }

    /// <summary>When a confirmation email was last sent, so the same address cannot be mailed over and over.</summary>
    public DateTimeOffset? ConfirmationSentAt { get; private set; }

    /// <summary>The last post this reader was told about. Lets an interrupted announcement be finished without anyone hearing twice.</summary>
    public Guid? LastNotifiedPostId { get; private set; }

    private Subscriber() { }

    public Subscriber(string email)
    {
        Email = Normalise(email);
    }

    public static string Normalise(string email)
    {
        var clean = (email ?? string.Empty).Trim().ToLowerInvariant();
        if (clean.Length == 0) throw new DomainException("Enter your email address.");
        if (clean.Length > MaxEmailLength) throw new DomainException("That email address is too long.");
        return clean;
    }

    /// <summary>Someone asked (again) to subscribe this address: it goes back to waiting for confirmation.</summary>
    public void RequestAgain()
    {
        if (Status == SubscriberStatus.Confirmed) return;
        Status = SubscriberStatus.Pending;
        Touch();
    }

    public void MarkConfirmationSent(DateTimeOffset at)
    {
        ConfirmationSentAt = at;
        Touch();
    }

    public void Confirm(DateTimeOffset at)
    {
        if (Status == SubscriberStatus.Confirmed) return;
        Status = SubscriberStatus.Confirmed;
        ConfirmedAt = at;
        Touch();
    }

    public void Unsubscribe()
    {
        if (Status == SubscriberStatus.Unsubscribed) return;
        Status = SubscriberStatus.Unsubscribed;
        Touch();
    }

    public void MarkNotified(Guid postId)
    {
        LastNotifiedPostId = postId;
        Touch();
    }
}
