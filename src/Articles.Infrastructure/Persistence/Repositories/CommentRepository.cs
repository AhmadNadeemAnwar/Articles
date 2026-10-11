using Articles.Domain.Entities;
using Articles.Domain.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace Articles.Infrastructure.Persistence.Repositories;

public class CommentRepository : ICommentRepository
{
    private readonly AppDbContext _db;

    public CommentRepository(AppDbContext db) => _db = db;

    public async Task<Comment?> GetByIdAsync(Guid id, CancellationToken ct = default) =>
        await _db.Comments
            .Include(c => c.Author)
            .Include(c => c.Replies).ThenInclude(r => r.Author)
            .FirstOrDefaultAsync(c => c.Id == id, ct);

    public async Task<IReadOnlyList<Comment>> GetThreadAsync(Guid postId, CancellationToken ct = default) =>
        await _db.Comments.AsNoTracking()
            .Where(c => c.PostId == postId && c.ParentId == null)
            .Include(c => c.Author)
            .Include(c => c.Replies).ThenInclude(r => r.Author)
            .OrderBy(c => c.CreatedAt)
            .AsSplitQuery()
            .ToListAsync(ct);

    public async Task AddAsync(Comment comment, CancellationToken ct = default) =>
        await _db.Comments.AddAsync(comment, ct);
}
