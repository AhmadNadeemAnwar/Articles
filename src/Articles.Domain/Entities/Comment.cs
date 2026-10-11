using Articles.Domain.Common;
using Articles.Domain.Exceptions;

namespace Articles.Domain.Entities;

public class Comment : BaseEntity
{
    public const int MaxBodyLength = 3000;

    public Guid PostId { get; private set; }
    public Post Post { get; private set; } = null!;
    public Guid AuthorId { get; private set; }
    public User Author { get; private set; } = null!;

    /// <summary>Set for replies, null for top-level comments. One level of nesting is enforced by the service.</summary>
    public Guid? ParentId { get; private set; }
    public Comment? Parent { get; private set; }
    public ICollection<Comment> Replies { get; private set; } = new List<Comment>();

    public string Body { get; private set; } = null!;
    public bool IsDeleted { get; private set; }

    private Comment() { }

    public Comment(Guid postId, Guid authorId, string body, Guid? parentId = null)
    {
        if (string.IsNullOrWhiteSpace(body)) throw new DomainException("Comment body is required.");
        if (body.Trim().Length > MaxBodyLength) throw new DomainException($"Comment cannot exceed {MaxBodyLength} characters.");

        PostId = postId;
        AuthorId = authorId;
        Body = body.Trim();
        ParentId = parentId;
    }

    public void Edit(string body, Guid editorId)
    {
        if (AuthorId != editorId) throw new ForbiddenException("You can only edit your own comments.");
        if (string.IsNullOrWhiteSpace(body)) throw new DomainException("Comment body is required.");
        Body = body.Trim();
        Touch();
    }

    /// <summary>Soft delete, so replies keep their place in the thread.</summary>
    public void SoftDelete()
    {
        IsDeleted = true;
        Body = string.Empty;
        Touch();
    }
}
