using Articles.Domain.Entities;

namespace Articles.Domain.Interfaces;

public interface IUserRepository
{
    Task<User?> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task<User?> GetByEmailAsync(string email, CancellationToken ct = default);
    Task<User?> GetByHandleAsync(string handle, CancellationToken ct = default);
    Task<bool> EmailExistsAsync(string email, CancellationToken ct = default);
    Task<bool> HandleExistsAsync(string handle, CancellationToken ct = default);
    Task AddAsync(User user, CancellationToken ct = default);

    Task<int> CountFollowersAsync(Guid userId, CancellationToken ct = default);
    Task<int> CountFollowingAsync(Guid userId, CancellationToken ct = default);
    Task<int> CountPublishedPostsAsync(Guid userId, CancellationToken ct = default);
}
