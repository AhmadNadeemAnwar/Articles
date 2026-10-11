using Articles.Application.Auth.Dtos;
using Articles.Application.Comments.Dtos;
using Articles.Application.Posts.Dtos;
using Articles.Application.Site;
using Articles.Domain.Entities;

namespace Articles.Application.Posts.Mapping;

public static class PostMappingExtensions
{
    public static AuthorSummaryDto ToAuthorSummary(this User user) =>
        new(user.Id, user.Handle, user.DisplayName, user.AvatarUrl, user.Bio);

    public static CurrentUserDto ToCurrentUser(this User user) =>
        new(user.Id, user.Email, user.Handle, user.DisplayName, user.Bio, user.AvatarUrl, user.WebsiteUrl);

    public static CategoryDto? ToDto(this Category? category) =>
        category is null ? null : new CategoryDto(category.Id, category.Name, category.Slug);

    public static TagDto ToDto(this Tag tag) => new(tag.Id, tag.Name, tag.Slug, tag.PostCount);

    public static PostSummaryDto ToSummary(this Post post) => new(
        post.Id,
        post.Slug,
        post.Title,
        post.Subtitle,
        post.BuildExcerpt(),
        post.CoverImageUrl,
        post.ReadingTimeMinutes,
        post.ClapCount,
        post.InsightfulCount,
        post.CommentCount,
        post.Status.ToString(),
        post.PublishedAt,
        post.Author.ToAuthorSummary(),
        post.PostTags.Where(pt => pt.Tag is not null).Select(pt => pt.Tag.ToDto()).ToList(),
        post.Category.ToDto());

    public static PostDetailDto ToDetail(this Post post, ViewerStateDto? viewer) => new(
        post.Id,
        post.Slug,
        post.Title,
        post.Subtitle,
        post.ContentJson,
        post.CoverImageUrl,
        post.ReadingTimeMinutes,
        post.ClapCount,
        post.InsightfulCount,
        post.CommentCount,
        post.ViewCount,
        post.Status.ToString(),
        post.PublishedAt,
        post.UpdatedAt,
        post.Author.ToAuthorSummary(),
        post.PostTags.Where(pt => pt.Tag is not null).Select(pt => pt.Tag.ToDto()).ToList(),
        post.Category.ToDto(),
        viewer);

    public static PostRevisionDto ToDto(this PostRevision revision) =>
        new(revision.Id, revision.Title, revision.CreatedAt);

    /// <summary>Maps a top-level comment and its replies. Replies are flat by design (one nesting level).</summary>
    public static CommentDto ToDto(this Comment comment) => new(
        comment.Id,
        comment.Body,
        comment.IsDeleted,
        comment.CreatedAt,
        comment.Author.ToAuthorSummary(),
        comment.Replies
            .OrderBy(r => r.CreatedAt)
            .Select(r => new CommentDto(r.Id, r.Body, r.IsDeleted, r.CreatedAt, r.Author.ToAuthorSummary(), []))
            .ToList());
}
