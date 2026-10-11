using Articles.Domain.Entities;
using Articles.Domain.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace Articles.Infrastructure.Persistence.Repositories;

public class EngagementRepository : IEngagementRepository
{
    private readonly AppDbContext _db;

    public EngagementRepository(AppDbContext db) => _db = db;

    public async Task<Bookmark?> GetBookmarkAsync(Guid postId, Guid userId, CancellationToken ct = default) =>
        await _db.Bookmarks.FirstOrDefaultAsync(b => b.PostId == postId && b.UserId == userId, ct);

    public async Task AddBookmarkAsync(Bookmark bookmark, CancellationToken ct = default) =>
        await _db.Bookmarks.AddAsync(bookmark, ct);

    public void RemoveBookmark(Bookmark bookmark) => _db.Bookmarks.Remove(bookmark);

    public async Task<UserFollow?> GetUserFollowAsync(Guid followerId, Guid followeeId, CancellationToken ct = default) =>
        await _db.UserFollows.FirstOrDefaultAsync(f => f.FollowerId == followerId && f.FolloweeId == followeeId, ct);

    public async Task AddUserFollowAsync(UserFollow follow, CancellationToken ct = default) =>
        await _db.UserFollows.AddAsync(follow, ct);

    public void RemoveUserFollow(UserFollow follow) => _db.UserFollows.Remove(follow);

    public async Task<TagFollow?> GetTagFollowAsync(Guid userId, Guid tagId, CancellationToken ct = default) =>
        await _db.TagFollows.FirstOrDefaultAsync(f => f.UserId == userId && f.TagId == tagId, ct);

    public async Task AddTagFollowAsync(TagFollow follow, CancellationToken ct = default) =>
        await _db.TagFollows.AddAsync(follow, ct);

    public void RemoveTagFollow(TagFollow follow) => _db.TagFollows.Remove(follow);

    public Task<bool> HasBookmarkedAsync(Guid postId, Guid userId, CancellationToken ct = default) =>
        _db.Bookmarks.AnyAsync(b => b.PostId == postId && b.UserId == userId, ct);
}
