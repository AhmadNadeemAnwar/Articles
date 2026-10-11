using Articles.Domain.Common;
using Articles.Domain.Entities;
using Articles.Domain.Enums;
using Articles.Domain.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace Articles.Infrastructure.Persistence.Repositories;

public class AdminRepository : IAdminRepository
{
    private const int TopCount = 5;

    private readonly AppDbContext _db;

    public AdminRepository(AppDbContext db) => _db = db;

    public async Task<AdminStats> GetStatsAsync(DateTimeOffset publishedSince, CancellationToken ct = default)
    {
        // Sequential on purpose: a DbContext does not support concurrent queries.
        var published = await _db.Posts.CountAsync(p => p.Status == PostStatus.Published, ct);
        var drafts = await _db.Posts.CountAsync(p => p.Status == PostStatus.Draft, ct);
        var inactive = await _db.Posts.CountAsync(p => p.Status == PostStatus.Inactive, ct);
        var users = await _db.Users.CountAsync(ct);
        var comments = await _db.Comments.CountAsync(c => !c.IsDeleted, ct);
        var claps = await _db.Posts.SumAsync(p => (long)p.ClapCount, ct);
        var insightful = await _db.Posts.SumAsync(p => (long)p.InsightfulCount, ct);
        var views = await _db.Posts.SumAsync(p => (long)p.ViewCount, ct);
        var bookmarks = await _db.Bookmarks.CountAsync(ct);
        var tags = await _db.Tags.CountAsync(ct);

        var topByViews = await _db.Posts.AsNoTracking()
            .Where(p => p.Status == PostStatus.Published)
            .OrderByDescending(p => p.ViewCount)
            .ThenByDescending(p => p.PublishedAt)
            .Take(TopCount)
            .Select(p => new TopPost(p.Id, p.Title, p.Slug, p.ViewCount, p.ClapCount, p.CommentCount))
            .ToListAsync(ct);

        var topByClaps = await _db.Posts.AsNoTracking()
            .Where(p => p.Status == PostStatus.Published)
            .OrderByDescending(p => p.ClapCount)
            .ThenByDescending(p => p.PublishedAt)
            .Take(TopCount)
            .Select(p => new TopPost(p.Id, p.Title, p.Slug, p.ViewCount, p.ClapCount, p.CommentCount))
            .ToListAsync(ct);

        var recent = await _db.Posts.AsNoTracking()
            .Where(p => p.Status == PostStatus.Published && p.PublishedAt >= publishedSince)
            .Select(p => p.PublishedAt)
            .ToListAsync(ct);

        return new AdminStats(
            published, drafts, inactive, users, comments, claps, insightful, views, bookmarks, tags,
            topByViews, topByClaps,
            recent.Where(d => d.HasValue).Select(d => d!.Value).ToList());
    }

    public async Task<PagedResult<Post>> SearchPostsAsync(PostStatus? status, string? search, int pageNumber, int pageSize, Guid? categoryId = null, bool uncategorised = false, CancellationToken ct = default)
    {
        var query = _db.Posts.AsNoTracking()
            .Include(p => p.Author)
            .Include(p => p.Category)
            .Include(p => p.PostTags).ThenInclude(pt => pt.Tag)
            .AsQueryable();

        if (status.HasValue) query = query.Where(p => p.Status == status.Value);

        if (categoryId.HasValue) query = query.Where(p => p.CategoryId == categoryId.Value);
        else if (uncategorised) query = query.Where(p => p.CategoryId == null);

        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim().ToLower();
            query = query.Where(p => p.Title.ToLower().Contains(term) || p.Author.Handle.ToLower().Contains(term));
        }

        query = query.OrderByDescending(p => p.UpdatedAt);

        var total = await query.CountAsync(ct);
        var items = await query.Skip((pageNumber - 1) * pageSize).Take(pageSize).AsSplitQuery().ToListAsync(ct);
        return new PagedResult<Post>(items, pageNumber, pageSize, total);
    }

    public async Task<PagedResult<Comment>> GetCommentsAsync(int pageNumber, int pageSize, CancellationToken ct = default)
    {
        var query = _db.Comments.AsNoTracking()
            .Include(c => c.Author)
            .Include(c => c.Post)
            .OrderByDescending(c => c.CreatedAt);

        var total = await query.CountAsync(ct);
        var items = await query.Skip((pageNumber - 1) * pageSize).Take(pageSize).ToListAsync(ct);
        return new PagedResult<Comment>(items, pageNumber, pageSize, total);
    }

    public async Task<IReadOnlyList<AdminTagRow>> GetTagsAsync(CancellationToken ct = default)
    {
        var rows = await _db.Tags.AsNoTracking()
            .OrderByDescending(t => t.PostCount)
            .ThenBy(t => t.Name)
            .Select(t => new { Tag = t, Followers = _db.TagFollows.Count(f => f.TagId == t.Id) })
            .ToListAsync(ct);

        return rows.Select(r => new AdminTagRow(r.Tag, r.Followers)).ToList();
    }

    public async Task<Tag?> GetTagAsync(Guid id, CancellationToken ct = default) =>
        await _db.Tags.FirstOrDefaultAsync(t => t.Id == id, ct);

    public async Task MergeTagsAsync(Guid sourceId, Guid targetId, CancellationToken ct = default)
    {
        var target = await _db.Tags.FirstAsync(t => t.Id == targetId, ct);
        var source = await _db.Tags.FirstAsync(t => t.Id == sourceId, ct);

        var targetPostIds = (await _db.PostTags.Where(pt => pt.TagId == targetId).Select(pt => pt.PostId).ToListAsync(ct)).ToHashSet();
        var sourceLinks = await _db.PostTags.Where(pt => pt.TagId == sourceId).ToListAsync(ct);

        foreach (var link in sourceLinks.Where(l => !targetPostIds.Contains(l.PostId)))
        {
            _db.PostTags.Add(new PostTag(link.PostId, targetId));
            targetPostIds.Add(link.PostId);
        }

        var targetFollowers = (await _db.TagFollows.Where(f => f.TagId == targetId).Select(f => f.UserId).ToListAsync(ct)).ToHashSet();
        var sourceFollows = await _db.TagFollows.Where(f => f.TagId == sourceId).ToListAsync(ct);

        foreach (var follow in sourceFollows.Where(f => !targetFollowers.Contains(f.UserId)))
        {
            _db.TagFollows.Add(new TagFollow(follow.UserId, targetId));
        }

        // PostCount only counts published posts, so recount from the merged set rather than adding the two figures.
        var ids = targetPostIds.ToList();
        var published = await _db.Posts.CountAsync(p => ids.Contains(p.Id) && p.Status == PostStatus.Published, ct);
        target.SetPostCount(published);

        _db.PostTags.RemoveRange(sourceLinks);
        _db.TagFollows.RemoveRange(sourceFollows);
        _db.Tags.Remove(source);
    }

    public void RemoveTag(Tag tag) => _db.Tags.Remove(tag);

    public async Task<IReadOnlyList<Post>> GetAllPostsAsync(CancellationToken ct = default) =>
        await _db.Posts.AsNoTracking()
            .Include(p => p.Author)
            .Include(p => p.Category)
            .Include(p => p.PostTags).ThenInclude(pt => pt.Tag)
            .OrderBy(p => p.CreatedAt)
            .AsSplitQuery()
            .ToListAsync(ct);
}
