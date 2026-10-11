using Articles.Domain.Enums;

namespace Articles.Domain.Entities;

/// <summary>
/// One visitor's reaction to one post. Readers have no accounts, so a visitor is known only by
/// <see cref="VisitorKey"/>: a one-way hash of a random id their browser made up. At most one row
/// exists per visitor, post and kind, which is what makes a reaction a toggle rather than a counter.
/// </summary>
public class Reaction
{
    public const int VisitorKeyLength = 64;

    public Guid PostId { get; private set; }
    public Post Post { get; private set; } = null!;
    public string VisitorKey { get; private set; } = null!;
    public ReactionKind Kind { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; } = DateTimeOffset.UtcNow;

    private Reaction() { }

    public Reaction(Guid postId, string visitorKey, ReactionKind kind)
    {
        PostId = postId;
        VisitorKey = visitorKey;
        Kind = kind;
    }
}

/// <summary>
/// Remembers that a visitor has already been counted as a reader of a post on a given day, so
/// refreshing the page does not inflate the view count. Rows older than a day are discarded.
/// </summary>
public class PostView
{
    public Guid PostId { get; private set; }
    public string VisitorKey { get; private set; } = null!;

    /// <summary>Days since 0001-01-01 (UTC), as <see cref="DateOnly.DayNumber"/>. A plain number sorts and compares the same on every database.</summary>
    public int Day { get; private set; }

    private PostView() { }

    public PostView(Guid postId, string visitorKey, int day)
    {
        PostId = postId;
        VisitorKey = visitorKey;
        Day = day;
    }
}
