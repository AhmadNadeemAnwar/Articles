using Articles.Application.Posts.Dtos;

namespace Articles.Application.Comments.Dtos;

public sealed record CommentDto(
    Guid Id,
    string Body,
    bool IsDeleted,
    DateTimeOffset CreatedAt,
    AuthorSummaryDto Author,
    IReadOnlyList<CommentDto> Replies);

public sealed record CreateCommentRequest(string Body, Guid? ParentId);

public sealed record UpdateCommentRequest(string Body);
