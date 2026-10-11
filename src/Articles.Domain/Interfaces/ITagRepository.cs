using Articles.Domain.Entities;

namespace Articles.Domain.Interfaces;

public interface ITagRepository
{
    Task<Tag?> GetBySlugAsync(string slug, CancellationToken ct = default);
    Task<IReadOnlyList<Tag>> GetBySlugsAsync(IEnumerable<string> slugs, CancellationToken ct = default);
    Task<IReadOnlyList<Tag>> GetPopularAsync(int limit, CancellationToken ct = default);
    Task<IReadOnlyList<Tag>> SearchAsync(string term, int limit, CancellationToken ct = default);
    Task<IReadOnlyList<Tag>> GetFollowedByAsync(Guid userId, CancellationToken ct = default);
    Task AddAsync(Tag tag, CancellationToken ct = default);
}
