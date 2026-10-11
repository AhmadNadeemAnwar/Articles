using Articles.Domain.Common;

namespace Articles.Domain.Entities;

/// <summary>Point-in-time snapshot of a post body, written on autosave so a draft can always be recovered.</summary>
public class PostRevision : BaseEntity
{
    public Guid PostId { get; private set; }
    public Post Post { get; private set; } = null!;
    public string Title { get; private set; } = null!;
    public string ContentJson { get; private set; } = null!;

    private PostRevision() { }

    public PostRevision(Guid postId, string title, string contentJson)
    {
        PostId = postId;
        Title = title;
        ContentJson = contentJson;
    }
}
