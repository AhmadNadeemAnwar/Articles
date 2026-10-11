using Articles.Domain.Common;
using Articles.Domain.Entities;
using Articles.Domain.Enums;
using Articles.Domain.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace Articles.Infrastructure.Persistence.Repositories;

public class PostRepository : IPostRepository
{
    /// <summary>Posts published within this window are eligible for the trending sort.</summary>
    private static readonly TimeSpan TrendingWindow = TimeSpan.FromDays(30);

    private readonly AppDbContext _db;

    public PostRepository(AppDbContext db) => _db = db;

    public async Task<Post?> GetByIdAsync(Guid id, CancellationToken ct = default) =>
        await _db.Posts
            .Include(p => p.Author)
            .Include(p => p.Category)
            .Include(p => p.PostTags).ThenInclude(pt => pt.Tag)
            .FirstOrDefaultAsync(p => p.Id == id, ct);

    public async Task<Post?> GetBySlugAsync(string slug, CancellationToken ct = default) =>
        await _db.Posts
            .Include(p => p.Author)
            .Include(p => p.Category)
            .Include(p => p.PostTags).ThenInclude(pt => pt.Tag)
            .FirstOrDefaultAsync(p => p.Slug == slug, ct);

    public async Task<bool> SlugExistsAsync(string slug, CancellationToken ct = default) =>
        await _db.Posts.AnyAsync(p => p.Slug == slug, ct);

    /// <summary>
    /// Browse and search over published posts.
    /// Matching uses a lower-cased substring match, which is portable and adequate at this scale. When the corpus
    /// outgrows it, this is the only method that changes: swap in a Postgres tsvector column
    /// with a GIN index and rank with ts_rank, keeping the same signature.
    /// </summary>
    public async Task<PagedResult<Post>> SearchAsync(PostQuery query, CancellationToken ct = default)
    {
        var posts = PublishedPosts();

        if (!string.IsNullOrWhiteSpace(query.SearchTerm))
        {
            // Lower-casing both sides keeps matching case-insensitive on SQLite and Postgres alike
            // (LIKE is case-insensitive on SQLite but case-sensitive on Postgres). Contains also
            // escapes wildcard characters itself, so a search for "100%" matches literally.
            var term = query.SearchTerm.ToLower();
            posts = posts.Where(p =>
                p.Title.ToLower().Contains(term)
                || (p.Subtitle != null && p.Subtitle.ToLower().Contains(term))
                || p.PlainText.ToLower().Contains(term));
        }

        if (!string.IsNullOrWhiteSpace(query.TagSlug))
        {
            posts = posts.Where(p => p.PostTags.Any(pt => pt.Tag.Slug == query.TagSlug));
        }

        if (!string.IsNullOrWhiteSpace(query.AuthorHandle))
        {
            posts = posts.Where(p => p.Author.Handle == query.AuthorHandle);
        }

        if (!string.IsNullOrWhiteSpace(query.CategorySlug))
        {
            posts = posts.Where(p => p.Category != null && p.Category.Slug == query.CategorySlug);
        }

        posts = ApplySort(posts, query.Sort, query.SearchTerm);

        return await PageAsync(posts, query.PageNumber, query.PageSize, ct);
    }

    public async Task<PagedResult<Post>> GetPersonalFeedAsync(Guid userId, int pageNumber, int pageSize, CancellationToken ct = default)
    {
        var followedAuthors = _db.UserFollows.Where(f => f.FollowerId == userId).Select(f => f.FolloweeId);
        var followedTags = _db.TagFollows.Where(f => f.UserId == userId).Select(f => f.TagId);

        var posts = PublishedPosts()
            .Where(p => followedAuthors.Contains(p.AuthorId)
                     || p.PostTags.Any(pt => followedTags.Contains(pt.TagId)))
            .OrderByDescending(p => p.PublishedAt);

        return await PageAsync(posts, pageNumber, pageSize, ct);
    }

    public async Task<PagedResult<Post>> GetDraftsAsync(Guid authorId, int pageNumber, int pageSize, CancellationToken ct = default)
    {
        var posts = BaseQuery()
            .Where(p => p.AuthorId == authorId && p.Status == PostStatus.Draft)
            .OrderByDescending(p => p.UpdatedAt);

        return await PageAsync(posts, pageNumber, pageSize, ct);
    }

    public async Task<PagedResult<Post>> GetBookmarkedAsync(Guid userId, int pageNumber, int pageSize, CancellationToken ct = default)
    {
        // Ordered by when the reader saved it, not when it was published.
        // The includes hang off the bookmark rather than the post: EF rejects an Include that
        // follows a Select, so the projection to Post has to happen after materialisation.
        var query = _db.Bookmarks.AsNoTracking()
            .Where(b => b.UserId == userId)
            .Include(b => b.Post).ThenInclude(p => p.Author)
            .Include(b => b.Post).ThenInclude(p => p.Category)
            .Include(b => b.Post).ThenInclude(p => p.PostTags).ThenInclude(pt => pt.Tag)
            .OrderByDescending(b => b.CreatedAt);

        var total = await query.CountAsync(ct);
        var bookmarks = await query
            .Skip((pageNumber - 1) * pageSize)
            .Take(pageSize)
            .AsSplitQuery()
            .ToListAsync(ct);

        return new PagedResult<Post>(bookmarks.Select(b => b.Post).ToList(), pageNumber, pageSize, total);
    }

    /// <summary>Ranks candidates by how many tags they share with the source post.</summary>
    public async Task<IReadOnlyList<Post>> GetRelatedAsync(Guid postId, int limit, CancellationToken ct = default)
    {
        var tagIds = await _db.PostTags
            .Where(pt => pt.PostId == postId)
            .Select(pt => pt.TagId)
            .ToListAsync(ct);

        if (tagIds.Count == 0) return [];

        return await PublishedPosts()
            .Where(p => p.Id != postId && p.PostTags.Any(pt => tagIds.Contains(pt.TagId)))
            .OrderByDescending(p => p.PostTags.Count(pt => tagIds.Contains(pt.TagId)))
            .ThenByDescending(p => p.ClapCount)
            .Take(limit)
            .AsSplitQuery()
            .ToListAsync(ct);
    }

    public async Task AddAsync(Post post, CancellationToken ct = default) =>
        await _db.Posts.AddAsync(post, ct);

    public void Remove(Post post) => _db.Posts.Remove(post);

    public async Task AddRevisionAsync(PostRevision revision, CancellationToken ct = default) =>
        await _db.PostRevisions.AddAsync(revision, ct);

    public async Task<IReadOnlyList<PostRevision>> GetRevisionsAsync(Guid postId, int limit, CancellationToken ct = default) =>
        await _db.PostRevisions.AsNoTracking()
            .Where(r => r.PostId == postId)
            .OrderByDescending(r => r.CreatedAt)
            .Take(limit)
            .ToListAsync(ct);

    public Task<PostRevision?> GetRevisionAsync(Guid postId, Guid revisionId, CancellationToken ct = default) =>
        _db.PostRevisions.AsNoTracking().FirstOrDefaultAsync(r => r.PostId == postId && r.Id == revisionId, ct);

    public async Task<DateTimeOffset?> GetLatestRevisionTimeAsync(Guid postId, CancellationToken ct = default) =>
        await _db.PostRevisions
            .Where(r => r.PostId == postId)
            .OrderByDescending(r => r.CreatedAt)
            .Select(r => (DateTimeOffset?)r.CreatedAt)
            .FirstOrDefaultAsync(ct);

    public async Task TrimRevisionsAsync(Guid postId, int keep, CancellationToken ct = default)
    {
        // Read the ids to drop first: "delete all but the newest N" as one statement is not portable.
        var surplus = await _db.PostRevisions
            .Where(r => r.PostId == postId)
            .OrderByDescending(r => r.CreatedAt)
            .Skip(keep)
            .Select(r => r.Id)
            .ToListAsync(ct);

        if (surplus.Count > 0)
            await _db.PostRevisions.Where(r => surplus.Contains(r.Id)).ExecuteDeleteAsync(ct);
    }

    private IQueryable<Post> BaseQuery() =>
        _db.Posts.AsNoTracking()
            .Include(p => p.Author)
            .Include(p => p.Category)
            .Include(p => p.PostTags).ThenInclude(pt => pt.Tag);

    private IQueryable<Post> PublishedPosts() =>
        BaseQuery().Where(p => p.Status == PostStatus.Published);

    private static IQueryable<Post> ApplySort(IQueryable<Post> posts, PostSort sort, string? searchTerm) => sort switch
    {
        PostSort.Popular => posts
            .OrderByDescending(p => p.ClapCount + (p.InsightfulCount * 2))
            .ThenByDescending(p => p.PublishedAt),

        // Recency-gated popularity: cheap to run and good enough to keep the front page moving.
        PostSort.Trending => posts
            .Where(p => p.PublishedAt >= DateTimeOffset.UtcNow - TrendingWindow)
            .OrderByDescending(p => (p.ClapCount * 3) + (p.InsightfulCount * 5) + p.ViewCount)
            .ThenByDescending(p => p.PublishedAt),

        // On a text search, title hits outrank body hits before falling back to recency.
        _ when !string.IsNullOrWhiteSpace(searchTerm) => posts
            .OrderByDescending(p => p.Title.ToLower().Contains(searchTerm.ToLower()))
            .ThenByDescending(p => p.PublishedAt),

        _ => posts.OrderByDescending(p => p.PublishedAt)
    };

    private static async Task<PagedResult<Post>> PageAsync(IQueryable<Post> posts, int pageNumber, int pageSize, CancellationToken ct)
    {
        var total = await posts.CountAsync(ct);
        var items = await posts
            .Skip((pageNumber - 1) * pageSize)
            .Take(pageSize)
            .AsSplitQuery()
            .ToListAsync(ct);

        return new PagedResult<Post>(items, pageNumber, pageSize, total);
    }
}
