using Articles.Domain.Entities;
using Articles.Domain.Enums;
using Articles.Domain.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace Articles.Infrastructure.Persistence.Repositories;

public class UserRepository : IUserRepository
{
    private readonly AppDbContext _db;

    public UserRepository(AppDbContext db) => _db = db;

    public async Task<User?> GetByIdAsync(Guid id, CancellationToken ct = default) =>
        await _db.Users.FirstOrDefaultAsync(u => u.Id == id, ct);

    public async Task<User?> GetByEmailAsync(string email, CancellationToken ct = default) =>
        await _db.Users.FirstOrDefaultAsync(u => u.Email == email, ct);

    public async Task<User?> GetByHandleAsync(string handle, CancellationToken ct = default) =>
        await _db.Users.FirstOrDefaultAsync(u => u.Handle == handle, ct);

    public async Task<bool> EmailExistsAsync(string email, CancellationToken ct = default) =>
        await _db.Users.AnyAsync(u => u.Email == email, ct);

    public async Task<bool> HandleExistsAsync(string handle, CancellationToken ct = default) =>
        await _db.Users.AnyAsync(u => u.Handle == handle, ct);

    public async Task AddAsync(User user, CancellationToken ct = default) =>
        await _db.Users.AddAsync(user, ct);

    public async Task<int> CountFollowersAsync(Guid userId, CancellationToken ct = default) =>
        await _db.UserFollows.CountAsync(f => f.FolloweeId == userId, ct);

    public async Task<int> CountFollowingAsync(Guid userId, CancellationToken ct = default) =>
        await _db.UserFollows.CountAsync(f => f.FollowerId == userId, ct);

    public async Task<int> CountPublishedPostsAsync(Guid userId, CancellationToken ct = default) =>
        await _db.Posts.CountAsync(p => p.AuthorId == userId && p.Status == PostStatus.Published, ct);
}
