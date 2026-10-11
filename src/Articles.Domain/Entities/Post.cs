using Articles.Domain.Common;
using Articles.Domain.Enums;
using Articles.Domain.Exceptions;

namespace Articles.Domain.Entities;

public class Post : BaseEntity
{
    public const int MaxTitleLength = 160;
    public const int MaxSubtitleLength = 300;
    private const int WordsReadPerMinute = 225;

    public Guid AuthorId { get; private set; }
    public User Author { get; private set; } = null!;

    public string Title { get; private set; } = null!;
    public string? Subtitle { get; private set; }

    /// <summary>
    /// Assigned once, at first publish, and never changed afterwards — editing a title must not
    /// break inbound links or the canonical URL.
    /// </summary>
    public string? Slug { get; private set; }

    public string? CoverImageUrl { get; private set; }

    /// <summary>The one shelf this post sits on, if any. Topics (PostTags) are separate and many.</summary>
    public Guid? CategoryId { get; private set; }
    public Category? Category { get; private set; }

    /// <summary>Editor document (ProseMirror/TipTap JSON). The source of truth for the post body.</summary>
    public string ContentJson { get; private set; } = null!;

    /// <summary>Server-derived flattening of <see cref="ContentJson"/>. Backs search, excerpts and reading time.</summary>
    public string PlainText { get; private set; } = string.Empty;

    public PostStatus Status { get; private set; } = PostStatus.Draft;
    public DateTimeOffset? PublishedAt { get; private set; }
    public int ReadingTimeMinutes { get; private set; } = 1;

    /// <summary>When subscribers were last emailed about this post; null if they never were.</summary>
    public DateTimeOffset? NotifiedAt { get; private set; }

    public int ClapCount { get; private set; }
    public int InsightfulCount { get; private set; }
    public int CommentCount { get; private set; }
    public int ViewCount { get; private set; }

    public ICollection<PostTag> PostTags { get; private set; } = new List<PostTag>();
    public ICollection<Comment> Comments { get; private set; } = new List<Comment>();
    public ICollection<PostRevision> Revisions { get; private set; } = new List<PostRevision>();

    private Post() { }

    public Post(Guid authorId, string title, string? subtitle, string contentJson, string plainText, string? coverImageUrl = null)
    {
        AuthorId = authorId;
        ApplyContent(title, subtitle, contentJson, plainText, coverImageUrl);
    }

    public void UpdateDraft(string title, string? subtitle, string contentJson, string plainText, string? coverImageUrl)
    {
        ApplyContent(title, subtitle, contentJson, plainText, coverImageUrl);
        Touch();
    }

    private void ApplyContent(string title, string? subtitle, string contentJson, string plainText, string? coverImageUrl)
    {
        if (string.IsNullOrWhiteSpace(title)) throw new DomainException("Title is required.");
        if (title.Trim().Length > MaxTitleLength) throw new DomainException($"Title cannot exceed {MaxTitleLength} characters.");
        if (subtitle is { Length: > MaxSubtitleLength }) throw new DomainException($"Subtitle cannot exceed {MaxSubtitleLength} characters.");
        if (string.IsNullOrWhiteSpace(contentJson)) throw new DomainException("Content is required.");
        if (!string.IsNullOrWhiteSpace(coverImageUrl) && !UrlRules.IsImageReference(coverImageUrl))
            throw new DomainException("Cover image must be an uploaded picture or a link starting with https://.");

        Title = title.Trim();
        Subtitle = string.IsNullOrWhiteSpace(subtitle) ? null : subtitle.Trim();
        ContentJson = contentJson;
        PlainText = plainText ?? string.Empty;
        CoverImageUrl = string.IsNullOrWhiteSpace(coverImageUrl) ? null : coverImageUrl.Trim();
        ReadingTimeMinutes = CalculateReadingTime(PlainText);
    }

    public void MarkNotified(DateTimeOffset at) => NotifiedAt = at;

    public void SetCategory(Guid? categoryId)
    {
        if (CategoryId == categoryId) return;
        CategoryId = categoryId;
        Category = null;
        Touch();
    }

    /// <param name="slug">Unique slug resolved by the application layer; ignored if this post already has one.</param>
    public void Publish(string slug)
    {
        if (Status == PostStatus.Published) throw new DomainException("Post is already published.");
        if (string.IsNullOrWhiteSpace(PlainText)) throw new DomainException("Cannot publish an empty post.");

        Slug ??= slug;
        Status = PostStatus.Published;
        PublishedAt ??= DateTimeOffset.UtcNow;
        Touch();
    }

    /// <summary>Switches the post off. It keeps its address, so publishing it again restores the same link.</summary>
    public void Deactivate()
    {
        if (Status == PostStatus.Inactive) throw new DomainException("Post is already not active.");
        Status = PostStatus.Inactive;
        Touch();
    }

    public void MoveToDraft()
    {
        if (Status == PostStatus.Draft) throw new DomainException("Post is already a draft.");
        Status = PostStatus.Draft;
        Touch();
    }

    /// <summary>Only a published post is public. Drafts and inactive posts are for their author alone.</summary>
    public bool IsVisibleTo(Guid? viewerId) =>
        Status == PostStatus.Published || (viewerId.HasValue && viewerId.Value == AuthorId);

    public void EnsureOwnedBy(Guid userId)
    {
        if (AuthorId != userId) throw new ForbiddenException("You can only modify your own posts.");
    }

    /// <summary>One visitor gave this reaction. The totals are kept here so lists never have to count rows.</summary>
    public void AddReaction(ReactionKind kind)
    {
        if (kind == ReactionKind.Clap) ClapCount++;
        else InsightfulCount++;
    }

    /// <summary>One visitor took their reaction back. A total never goes below zero.</summary>
    public void RemoveReaction(ReactionKind kind)
    {
        if (kind == ReactionKind.Clap) ClapCount = Math.Max(0, ClapCount - 1);
        else InsightfulCount = Math.Max(0, InsightfulCount - 1);
    }

    public void IncrementCommentCount() => CommentCount++;
    public void DecrementCommentCount() => CommentCount = Math.Max(0, CommentCount - 1);
    public void RegisterView() => ViewCount++;

    /// <summary>Excerpt used in feed cards and as the meta description.</summary>
    public string BuildExcerpt(int maxLength = 200)
    {
        if (!string.IsNullOrWhiteSpace(Subtitle)) return Subtitle;
        if (PlainText.Length <= maxLength) return PlainText;

        var cut = PlainText[..maxLength];
        var lastSpace = cut.LastIndexOf(' ');
        return (lastSpace > 0 ? cut[..lastSpace] : cut) + "...";
    }

    private static int CalculateReadingTime(string plainText)
    {
        if (string.IsNullOrWhiteSpace(plainText)) return 1;
        var words = plainText.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).Length;
        return Math.Max(1, (int)Math.Ceiling(words / (double)WordsReadPerMinute));
    }
}
