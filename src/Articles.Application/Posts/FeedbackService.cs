using Articles.Application.Admin;
using Articles.Domain.Common;
using Articles.Domain.Entities;
using Articles.Domain.Exceptions;
using Articles.Domain.Interfaces;

namespace Articles.Application.Posts;

public sealed record SubmitFeedbackRequest(IReadOnlyList<string>? Liked, string? Text);

public sealed record FeedbackDto(Guid Id, DateTimeOffset CreatedAt, Guid PostId, string PostTitle, string? PostSlug, IReadOnlyList<string> Liked, string Text);

public interface IFeedbackService
{
    Task SubmitAsync(Guid postId, SubmitFeedbackRequest request, CancellationToken ct = default);
    Task<PagedResult<FeedbackDto>> GetPageAsync(int pageNumber, int pageSize, CancellationToken ct = default);
    Task DeleteAsync(Guid id, string admin, CancellationToken ct = default);
}

/// <summary>Readers' short notes after reacting to a post, and the owner's list of them. Anonymous: nothing about the reader is kept.</summary>
public sealed class FeedbackService : IFeedbackService
{
    private readonly IFeedbackRepository _feedback;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IActivityLog _activity;

    public FeedbackService(IFeedbackRepository feedback, IUnitOfWork unitOfWork, IActivityLog? activity = null)
    {
        _feedback = feedback;
        _unitOfWork = unitOfWork;
        _activity = activity ?? NoActivityLog.Instance;
    }

    public async Task SubmitAsync(Guid postId, SubmitFeedbackRequest request, CancellationToken ct = default)
    {
        if (!await _feedback.IsPublishedAsync(postId, ct)) throw new NotFoundException(nameof(Post), postId);

        await _feedback.AddAsync(new ReaderFeedback(postId, request.Liked, request.Text), ct);
        await _unitOfWork.SaveChangesAsync(ct);
    }

    public async Task<PagedResult<FeedbackDto>> GetPageAsync(int pageNumber, int pageSize, CancellationToken ct = default)
    {
        var page = await _feedback.GetPageAsync(pageNumber, pageSize, ct);
        return new PagedResult<FeedbackDto>(
            page.Items.Select(f => new FeedbackDto(f.Id, f.CreatedAt, f.PostId, f.Post.Title, f.Post.Slug, f.LikedList, f.Text)).ToList(),
            page.PageNumber, page.PageSize, page.TotalCount);
    }

    public async Task DeleteAsync(Guid id, string admin, CancellationToken ct = default)
    {
        var feedback = await _feedback.GetAsync(id, ct) ?? throw new NotFoundException(nameof(ReaderFeedback), id);
        _feedback.Remove(feedback);
        await _unitOfWork.SaveChangesAsync(ct);
        await _activity.RecordAsync(admin, Activity.DeletedFeedback, $"on {feedback.Post.Title}", ct);
    }
}
