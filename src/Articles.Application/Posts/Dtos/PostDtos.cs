using Articles.Application.Site;
using Articles.Domain.Common;

namespace Articles.Application.Posts.Dtos;

public sealed record AuthorSummaryDto(
    Guid Id,
    string Handle,
    string DisplayName,
    string? AvatarUrl,
    string? Bio);

public sealed record TagDto(
    Guid Id,
    string Name,
    string Slug,
    int PostCount);

/// <summary>Feed-card shape. Deliberately excludes the post body so list endpoints stay light.</summary>
public sealed record PostSummaryDto(
    Guid Id,
    string? Slug,
    string Title,
    string? Subtitle,
    string Excerpt,
    string? CoverImageUrl,
    int ReadingTimeMinutes,
    int ClapCount,
    int InsightfulCount,
    int CommentCount,
    string Status,
    DateTimeOffset? PublishedAt,
    AuthorSummaryDto Author,
    IReadOnlyList<TagDto> Tags,
    CategoryDto? Category);

/// <summary>Flags for a signed-in account, null for visitors. A visitor's reactions come from the reactions route instead.</summary>
public sealed record ViewerStateDto(bool HasBookmarked, bool IsFollowingAuthor, bool IsAuthor);

public sealed record PostDetailDto(
    Guid Id,
    string? Slug,
    string Title,
    string? Subtitle,
    string ContentJson,
    string? CoverImageUrl,
    int ReadingTimeMinutes,
    int ClapCount,
    int InsightfulCount,
    int CommentCount,
    int ViewCount,
    string Status,
    DateTimeOffset? PublishedAt,
    DateTimeOffset UpdatedAt,
    AuthorSummaryDto Author,
    IReadOnlyList<TagDto> Tags,
    CategoryDto? Category,
    ViewerStateDto? Viewer);

/// <summary>Draft, Published or Inactive.</summary>
public sealed record SetPostStatusRequest(string Status);

public sealed record CreatePostRequest(
    string Title,
    string? Subtitle,
    string ContentJson,
    string? CoverImageUrl,
    IReadOnlyList<string>? Tags,
    Guid? CategoryId = null);

public sealed record UpdatePostRequest(
    string Title,
    string? Subtitle,
    string ContentJson,
    string? CoverImageUrl,
    IReadOnlyList<string>? Tags,
    Guid? CategoryId = null);

public sealed record PostRevisionDto(Guid Id, string Title, DateTimeOffset CreatedAt);

/// <summary>A saved revision with its body, for putting an earlier version back into the editor.</summary>
public sealed record PostRevisionDetailDto(Guid Id, string Title, string ContentJson, DateTimeOffset CreatedAt);

/// <summary>Query-string binding for the browse/search endpoint.</summary>
public sealed class PostQueryParameters
{
    private const int MaxPageSize = 50;
    private int _pageSize = 20;
    private int _pageNumber = 1;

    /// <summary>Free-text search term matched against title, subtitle and body.</summary>
    public string? Q { get; set; }
    public string? Tag { get; set; }
    public string? Author { get; set; }
    public string? Category { get; set; }
    public PostSort Sort { get; set; } = PostSort.Latest;

    public int PageNumber
    {
        get => _pageNumber;
        set => _pageNumber = value < 1 ? 1 : value;
    }

    public int PageSize
    {
        get => _pageSize;
        set => _pageSize = value switch { < 1 => 20, > MaxPageSize => MaxPageSize, _ => value };
    }

    public PostQuery ToQuery() => new()
    {
        SearchTerm = string.IsNullOrWhiteSpace(Q) ? null : Q.Trim(),
        TagSlug = string.IsNullOrWhiteSpace(Tag) ? null : Tag.Trim().ToLowerInvariant(),
        AuthorHandle = string.IsNullOrWhiteSpace(Author) ? null : Author.Trim().ToLowerInvariant(),
        CategorySlug = string.IsNullOrWhiteSpace(Category) ? null : Category.Trim().ToLowerInvariant(),
        Sort = Sort,
        PageNumber = PageNumber,
        PageSize = PageSize
    };
}
