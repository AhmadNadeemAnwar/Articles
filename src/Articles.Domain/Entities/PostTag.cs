namespace Articles.Domain.Entities;

/// <summary>Join entity for the many-to-many between posts and tags.</summary>
public class PostTag
{
    public Guid PostId { get; private set; }
    public Post Post { get; private set; } = null!;
    public Guid TagId { get; private set; }
    public Tag Tag { get; private set; } = null!;

    private PostTag() { }

    public PostTag(Guid postId, Guid tagId)
    {
        PostId = postId;
        TagId = tagId;
    }
}
