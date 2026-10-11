namespace Articles.Domain.Common;

public enum PostSort
{
    /// <summary>Newest first by publish date.</summary>
    Latest = 0,
    /// <summary>Highest engagement first.</summary>
    Popular = 1,
    /// <summary>Engagement decayed by age, so new posts can surface.</summary>
    Trending = 2
}

/// <summary>
/// Query specification for browsing and searching published posts. Kept in the domain so the
/// repository contract does not depend on the application layer.
/// </summary>
public sealed record PostQuery
{
    public string? SearchTerm { get; init; }
    public string? TagSlug { get; init; }
    public string? AuthorHandle { get; init; }
    public string? CategorySlug { get; init; }
    public PostSort Sort { get; init; } = PostSort.Latest;
    public int PageNumber { get; init; } = 1;
    public int PageSize { get; init; } = 20;
}
