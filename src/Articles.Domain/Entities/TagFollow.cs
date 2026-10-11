namespace Articles.Domain.Entities;

/// <summary>
/// Topic subscription. Lets a brand-new reader build a useful feed on day one,
/// before they follow any writers.
/// </summary>
public class TagFollow
{
    public Guid UserId { get; private set; }
    public User User { get; private set; } = null!;
    public Guid TagId { get; private set; }
    public Tag Tag { get; private set; } = null!;
    public DateTimeOffset CreatedAt { get; private set; } = DateTimeOffset.UtcNow;

    private TagFollow() { }

    public TagFollow(Guid userId, Guid tagId)
    {
        UserId = userId;
        TagId = tagId;
    }
}
