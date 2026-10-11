using Articles.Application.Common;
using Articles.Application.Posts.Dtos;
using Articles.Application.Posts.Mapping;
using Articles.Domain.Common;
using Articles.Domain.Entities;
using Articles.Domain.Enums;
using Articles.Domain.Exceptions;
using Articles.Domain.Interfaces;

namespace Articles.Application.Posts;

public sealed class PostService : IPostService
{
    private const int MaxTagsPerPost = 5;

    /// <summary>
    /// The editor saves every few seconds, so a snapshot per save would be thousands of near-identical
    /// copies. One is kept at most this often, which is still fine-grained enough to undo a bad edit.
    /// </summary>
    public static readonly TimeSpan RevisionInterval = TimeSpan.FromMinutes(10);

    /// <summary>Older snapshots beyond this many are dropped, to stay inside the free database allowance.</summary>
    public const int MaxRevisionsPerPost = 30;

    private readonly IPostRepository _posts;
    private readonly ITagRepository _tags;
    private readonly IEngagementRepository _engagement;
    private readonly IUnitOfWork _unitOfWork;
    private readonly TimeProvider _time;
    private readonly ISiteRepository? _site;

    public PostService(
        IPostRepository posts,
        ITagRepository tags,
        IEngagementRepository engagement,
        IUnitOfWork unitOfWork,
        TimeProvider? time = null,
        ISiteRepository? site = null)
    {
        _site = site;
        _posts = posts;
        _tags = tags;
        _engagement = engagement;
        _unitOfWork = unitOfWork;
        _time = time ?? TimeProvider.System;
    }

    public async Task<PostDetailDto> CreateDraftAsync(CreatePostRequest request, Guid authorId, CancellationToken ct = default)
    {
        PostContent.Validate(request.ContentJson);
        var plainText = ProseMirrorText.Extract(request.ContentJson);
        var post = new Post(authorId, request.Title, request.Subtitle, request.ContentJson, plainText, request.CoverImageUrl);

        await ApplyCategoryAsync(post, request.CategoryId, ct);

        await _posts.AddAsync(post, ct);
        await SyncTagsAsync(post, request.Tags, ct);
        await _unitOfWork.SaveChangesAsync(ct);

        return await LoadDetailAsync(post.Id, authorId, ct);
    }

    public async Task<PostDetailDto> UpdateAsync(Guid id, UpdatePostRequest request, Guid authorId, CancellationToken ct = default)
    {
        var post = await _posts.GetByIdAsync(id, ct) ?? throw new NotFoundException(nameof(Post), id);
        post.EnsureOwnedBy(authorId);

        PostContent.Validate(request.ContentJson);

        // Snapshot the pre-edit version first, so a bad edit is recoverable. Skipped when nothing a
        // snapshot holds is changing, and when one was taken recently (see RevisionInterval).
        var changes = post.Title != request.Title.Trim() || post.ContentJson != request.ContentJson;
        var snapshot = false;
        if (changes)
        {
            var latest = await _posts.GetLatestRevisionTimeAsync(post.Id, ct);
            snapshot = latest is null || _time.GetUtcNow() - latest.Value >= RevisionInterval;
            if (snapshot) await _posts.AddRevisionAsync(new PostRevision(post.Id, post.Title, post.ContentJson), ct);
        }

        var plainText = ProseMirrorText.Extract(request.ContentJson);
        post.UpdateDraft(request.Title, request.Subtitle, request.ContentJson, plainText, request.CoverImageUrl);
        await ApplyCategoryAsync(post, request.CategoryId, ct);

        await SyncTagsAsync(post, request.Tags, ct);
        await _unitOfWork.SaveChangesAsync(ct);

        if (snapshot) await _posts.TrimRevisionsAsync(post.Id, MaxRevisionsPerPost, ct);

        return await LoadDetailAsync(post.Id, authorId, ct);
    }

    public Task<PostDetailDto> PublishAsync(Guid id, Guid authorId, CancellationToken ct = default) =>
        ChangeStatusAsync(id, PostStatus.Published, authorId, ct);

    public Task<PostDetailDto> UnpublishAsync(Guid id, Guid authorId, CancellationToken ct = default) =>
        ChangeStatusAsync(id, PostStatus.Inactive, authorId, ct);

    public Task<PostDetailDto> SetStatusAsync(Guid id, string status, Guid authorId, CancellationToken ct = default) =>
        ChangeStatusAsync(id, PostStatusChange.Parse(status), authorId, ct);

    private async Task<PostDetailDto> ChangeStatusAsync(Guid id, PostStatus status, Guid authorId, CancellationToken ct)
    {
        var post = await _posts.GetByIdAsync(id, ct) ?? throw new NotFoundException(nameof(Post), id);
        post.EnsureOwnedBy(authorId);

        await PostStatusChange.ApplyAsync(post, status, _posts, ct);

        await _unitOfWork.SaveChangesAsync(ct);
        return await LoadDetailAsync(post.Id, authorId, ct);
    }

    public async Task DeleteAsync(Guid id, Guid authorId, CancellationToken ct = default)
    {
        var post = await _posts.GetByIdAsync(id, ct) ?? throw new NotFoundException(nameof(Post), id);
        post.EnsureOwnedBy(authorId);

        if (post.Status == PostStatus.Published)
        {
            foreach (var postTag in post.PostTags)
            {
                postTag.Tag?.DecrementPostCount();
            }
        }

        _posts.Remove(post);
        await _unitOfWork.SaveChangesAsync(ct);
    }

    public async Task<PostDetailDto> GetBySlugAsync(string slug, Guid? viewerId, CancellationToken ct = default)
    {
        var post = await _posts.GetBySlugAsync(slug, ct) ?? throw new NotFoundException(nameof(Post), slug);

        if (!post.IsVisibleTo(viewerId)) throw new NotFoundException(nameof(Post), slug);

        // Fetching a post is not counted as a read: crawlers and refreshes fetch too. The reader's
        // browser reports a view separately, once per visitor per day (see ReactionService).
        return post.ToDetail(await BuildViewerStateAsync(post, viewerId, ct));
    }

    public async Task<PostDetailDto> GetForEditAsync(Guid id, Guid authorId, CancellationToken ct = default)
    {
        var post = await _posts.GetByIdAsync(id, ct) ?? throw new NotFoundException(nameof(Post), id);
        post.EnsureOwnedBy(authorId);
        return post.ToDetail(await BuildViewerStateAsync(post, authorId, ct));
    }

    public async Task<PagedResult<PostSummaryDto>> SearchAsync(PostQueryParameters parameters, CancellationToken ct = default)
    {
        var result = await _posts.SearchAsync(parameters.ToQuery(), ct);
        return result.Map(p => p.ToSummary());
    }

    public async Task<PagedResult<PostSummaryDto>> GetPersonalFeedAsync(Guid userId, int pageNumber, int pageSize, CancellationToken ct = default)
    {
        var result = await _posts.GetPersonalFeedAsync(userId, pageNumber, pageSize, ct);
        return result.Map(p => p.ToSummary());
    }

    public async Task<PagedResult<PostSummaryDto>> GetDraftsAsync(Guid authorId, int pageNumber, int pageSize, CancellationToken ct = default)
    {
        var result = await _posts.GetDraftsAsync(authorId, pageNumber, pageSize, ct);
        return result.Map(p => p.ToSummary());
    }

    public async Task<PagedResult<PostSummaryDto>> GetBookmarksAsync(Guid userId, int pageNumber, int pageSize, CancellationToken ct = default)
    {
        var result = await _posts.GetBookmarkedAsync(userId, pageNumber, pageSize, ct);
        return result.Map(p => p.ToSummary());
    }

    public async Task<IReadOnlyList<PostSummaryDto>> GetRelatedAsync(Guid postId, int limit = 4, CancellationToken ct = default)
    {
        var related = await _posts.GetRelatedAsync(postId, limit, ct);
        return related.Select(p => p.ToSummary()).ToList();
    }

    public async Task<IReadOnlyList<PostRevisionDto>> GetRevisionsAsync(Guid postId, Guid authorId, int limit = 20, CancellationToken ct = default)
    {
        var post = await _posts.GetByIdAsync(postId, ct) ?? throw new NotFoundException(nameof(Post), postId);
        post.EnsureOwnedBy(authorId);

        var revisions = await _posts.GetRevisionsAsync(postId, limit, ct);
        return revisions.Select(r => r.ToDto()).ToList();
    }

    public async Task<PostRevisionDetailDto> GetRevisionAsync(Guid postId, Guid revisionId, Guid authorId, CancellationToken ct = default)
    {
        var post = await _posts.GetByIdAsync(postId, ct) ?? throw new NotFoundException(nameof(Post), postId);
        post.EnsureOwnedBy(authorId);

        var revision = await _posts.GetRevisionAsync(postId, revisionId, ct) ?? throw new NotFoundException(nameof(PostRevision), revisionId);
        return new PostRevisionDetailDto(revision.Id, revision.Title, revision.ContentJson, revision.CreatedAt);
    }

    /// <summary>Puts the post on a shelf, or takes it off. A category that does not exist is refused, not ignored.</summary>
    private async Task ApplyCategoryAsync(Post post, Guid? categoryId, CancellationToken ct)
    {
        if (categoryId is { } id && id != post.CategoryId)
        {
            if (_site is null || await _site.GetCategoryAsync(id, ct) is null)
                throw new DomainException("That category no longer exists. Choose another, or none.");
        }

        post.SetCategory(categoryId);
    }

    /// <summary>
    /// Reconciles a post's tags against the submitted names, creating any that do not exist yet.
    /// Tag post-counts are only adjusted for published posts, since drafts are not listed.
    /// </summary>
    private async Task SyncTagsAsync(Post post, IReadOnlyList<string>? tagNames, CancellationToken ct)
    {
        var requested = (tagNames ?? [])
            .Where(t => !string.IsNullOrWhiteSpace(t))
            .Select(t => (Name: t.Trim(), Slug: SlugGenerator.Generate(t)))
            .Where(t => t.Slug.Length > 0)
            .DistinctBy(t => t.Slug)
            .Take(MaxTagsPerPost)
            .ToList();

        if (tagNames is { Count: > MaxTagsPerPost })
            throw new DomainException($"A post can have at most {MaxTagsPerPost} tags.");

        var existing = await _tags.GetBySlugsAsync(requested.Select(t => t.Slug), ct);
        var bySlug = existing.ToDictionary(t => t.Slug, StringComparer.OrdinalIgnoreCase);

        foreach (var (name, slug) in requested)
        {
            if (bySlug.ContainsKey(slug)) continue;

            var tag = new Tag(name, slug);
            await _tags.AddAsync(tag, ct);
            bySlug[slug] = tag;
        }

        var isPublished = post.Status == PostStatus.Published;
        var desiredIds = requested.Select(t => bySlug[t.Slug].Id).ToHashSet();

        foreach (var removed in post.PostTags.Where(pt => !desiredIds.Contains(pt.TagId)).ToList())
        {
            if (isPublished) removed.Tag?.DecrementPostCount();
            post.PostTags.Remove(removed);
        }

        var currentIds = post.PostTags.Select(pt => pt.TagId).ToHashSet();
        foreach (var (_, slug) in requested)
        {
            var tag = bySlug[slug];
            if (currentIds.Contains(tag.Id)) continue;

            post.PostTags.Add(new PostTag(post.Id, tag.Id));
            if (isPublished) tag.IncrementPostCount();
        }
    }

    private async Task<ViewerStateDto?> BuildViewerStateAsync(Post post, Guid? viewerId, CancellationToken ct)
    {
        if (viewerId is not { } userId) return null;

        var hasBookmarked = await _engagement.HasBookmarkedAsync(post.Id, userId, ct);
        var isFollowing = post.AuthorId != userId
            && await _engagement.GetUserFollowAsync(userId, post.AuthorId, ct) is not null;

        return new ViewerStateDto(hasBookmarked, isFollowing, post.AuthorId == userId);
    }

    private async Task<PostDetailDto> LoadDetailAsync(Guid postId, Guid viewerId, CancellationToken ct)
    {
        var post = await _posts.GetByIdAsync(postId, ct) ?? throw new NotFoundException(nameof(Post), postId);
        return post.ToDetail(await BuildViewerStateAsync(post, viewerId, ct));
    }
}
