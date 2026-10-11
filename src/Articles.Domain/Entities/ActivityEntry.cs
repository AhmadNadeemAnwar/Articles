namespace Articles.Domain.Entities;

/// <summary>
/// One line of the admin portal's history: who did what, to what, and when. Written in plain
/// words because it is read by the site's owner, not parsed by a program.
/// </summary>
public class ActivityEntry
{
    public const int MaxActorLength = 254;
    public const int MaxActionLength = 80;
    public const int MaxSubjectLength = 300;

    public Guid Id { get; private set; } = Guid.NewGuid();
    public DateTimeOffset At { get; private set; }
    public string Actor { get; private set; } = null!;
    public string Action { get; private set; } = null!;
    public string Subject { get; private set; } = string.Empty;

    private ActivityEntry() { }

    public ActivityEntry(DateTimeOffset at, string actor, string action, string? subject)
    {
        At = at;
        Actor = Fit(actor, MaxActorLength);
        Action = Fit(action, MaxActionLength);
        Subject = Fit(subject ?? string.Empty, MaxSubjectLength);
    }

    /// <summary>A long post title must never stop its own action being recorded.</summary>
    private static string Fit(string value, int max)
    {
        var trimmed = value.Trim();
        return trimmed.Length <= max ? trimmed : trimmed[..(max - 1)] + "…";
    }
}
