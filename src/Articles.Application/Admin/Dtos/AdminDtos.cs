namespace Articles.Application.Admin.Dtos;

public sealed record AdminLoginRequest(string Email, string Code);

public sealed record AdminSessionDto(string Token, DateTimeOffset ExpiresAt, string Email, string DisplayName);

public sealed record DailyCountDto(string Date, int Count);

/// <summary>A full copy of every post, in a form that can be read without this software.</summary>
public sealed record ExportDto(string Site, int FormatVersion, DateTimeOffset ExportedAt, int PostCount, IReadOnlyList<ExportedPostDto> Posts);

public sealed record ExportedPostDto(
    Guid Id,
    string Title,
    string? Subtitle,
    string? Slug,
    string Status,
    string AuthorHandle,
    string AuthorName,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    DateTimeOffset? PublishedAt,
    IReadOnlyList<string> Tags,
    string? Category,
    string? CoverImageUrl,
    int Views,
    int Claps,
    int Insightful,
    /// <summary>The body as plain words, readable anywhere.</summary>
    string PlainText,
    /// <summary>The body exactly as stored, with its sections and formatting, for restoring into Articles.</summary>
    string ContentJson);

public sealed record TopPostDto(Guid Id, string Title, string? Slug, int Views, int Claps, int Comments);

public sealed record AdminStatsDto(
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
    IReadOnlyList<TopPostDto> TopByViews,
    IReadOnlyList<TopPostDto> TopByClaps,
    IReadOnlyList<DailyCountDto> PublishedLast30Days);

public sealed record AdminPostDto(
    Guid Id,
    string Title,
    string? Slug,
    string Status,
    string AuthorHandle,
    string AuthorName,
    DateTimeOffset? PublishedAt,
    DateTimeOffset UpdatedAt,
    int Views,
    int Claps,
    int Insightful,
    int Comments,
    IReadOnlyList<string> Tags,
    string? Category,
    DateTimeOffset? NotifiedAt);

public sealed record AdminCommentDto(
    Guid Id,
    string Body,
    bool IsDeleted,
    bool IsReply,
    DateTimeOffset CreatedAt,
    string AuthorHandle,
    Guid PostId,
    string PostTitle,
    string? PostSlug);

public sealed record AdminTagDto(Guid Id, string Name, string Slug, int PostCount, int Followers);

public sealed record RenameTagRequest(string Name);

public sealed record MergeTagRequest(Guid TargetTagId);
