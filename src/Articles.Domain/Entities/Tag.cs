using Articles.Domain.Common;
using Articles.Domain.Exceptions;

namespace Articles.Domain.Entities;

/// <summary>A topic/domain label. Tags are global and shared across posts.</summary>
public class Tag : BaseEntity
{
    public string Name { get; private set; } = null!;
    public string Slug { get; private set; } = null!;
    /// <summary>Denormalised count of published posts, kept fresh on publish/unpublish so tag listings stay cheap.</summary>
    public int PostCount { get; private set; }

    public ICollection<PostTag> PostTags { get; private set; } = new List<PostTag>();

    private Tag() { }

    public Tag(string name, string slug)
    {
        if (string.IsNullOrWhiteSpace(name)) throw new DomainException("Tag name is required.");
        if (name.Trim().Length > 40) throw new DomainException("Tag name cannot exceed 40 characters.");

        Name = name.Trim();
        Slug = slug;
    }

    /// <summary>Changes the display name only. The slug is deliberately left alone so existing /tag/ links keep working.</summary>
    public void Rename(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) throw new DomainException("Tag name is required.");
        if (name.Trim().Length > 40) throw new DomainException("Tag name cannot exceed 40 characters.");

        Name = name.Trim();
        Touch();
    }

    public void SetPostCount(int count)
    {
        PostCount = Math.Max(0, count);
        Touch();
    }

    public void IncrementPostCount() { PostCount++; Touch(); }
    public void DecrementPostCount() { if (PostCount > 0) PostCount--; Touch(); }
}
