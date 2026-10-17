using Articles.Domain.Common;
using Articles.Domain.Entities;

namespace Articles.Domain.Interfaces;

public interface IFeedbackRepository
{
    Task AddAsync(ReaderFeedback feedback, CancellationToken ct = default);

    /// <summary>Newest first, with the post each one is about loaded alongside.</summary>
    Task<PagedResult<ReaderFeedback>> GetPageAsync(int pageNumber, int pageSize, CancellationToken ct = default);

    Task<ReaderFeedback?> GetAsync(Guid id, CancellationToken ct = default);
    void Remove(ReaderFeedback feedback);

    /// <summary>Feedback is only taken for a post readers can actually see.</summary>
    Task<bool> IsPublishedAsync(Guid postId, CancellationToken ct = default);
}
