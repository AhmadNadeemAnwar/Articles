namespace Articles.Domain.Entities;

/// <summary>A post saved to a reader's reading list.</summary>
public class Bookmark
{
    public Guid PostId { get; private set; }
    public Post Post { get; private set; } = null!;
    public Guid UserId { get; private set; }
    public User User { get; private set; } = null!;
    public DateTimeOffset CreatedAt { get; private set; } = DateTimeOffset.UtcNow;

    private Bookmark() { }

    public Bookmark(Guid postId, Guid userId)
    {
        PostId = postId;
        UserId = userId;
    }
}
