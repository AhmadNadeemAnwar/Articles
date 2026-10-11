using Articles.Domain.Entities;

namespace Articles.Domain.Common;

public sealed record TopPost(Guid Id, string Title, string? Slug, int Views, int Claps, int Comments);

/// <summary>Raw site-wide figures for the admin dashboard.</summary>
public sealed record AdminStats(
    int PublishedPosts,
    int DraftPosts,
    int InactivePosts,
    int Users,
    int Comments,
    long Claps,
    long Insightful,
    long Views,
    int Bookmarks,
    int Tags,
    IReadOnlyList<TopPost> TopByViews,
    IReadOnlyList<TopPost> TopByClaps,
    IReadOnlyList<DateTimeOffset> RecentPublishDates);

public sealed record AdminTagRow(Tag Tag, int Followers);
