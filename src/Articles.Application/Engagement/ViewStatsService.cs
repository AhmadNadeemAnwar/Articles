using Articles.Domain.Exceptions;
using Articles.Domain.Interfaces;

namespace Articles.Application.Engagement;

public sealed record DayViews(string Date, int Views, int Returning);

/// <param name="Key">A country code, "phone"/"tablet"/"computer", or a site name. Empty means not known (or, for sources, came straight in).</param>
public sealed record ViewShare(string Key, int Views);

public sealed record PostViews(Guid PostId, string Title, string? Slug, int Views);

/// <param name="Daily">One entry per day of the period, oldest first, including days with no readers.</param>
/// <param name="TopPosts">The most read posts in the period; empty when looking at a single post.</param>
/// <param name="AllTime">Every reader ever counted, including those from before daily figures were kept.</param>
public sealed record ViewStatsDto(
    int Days, int Views, int Returning,
    IReadOnlyList<DayViews> Daily,
    IReadOnlyList<ViewShare> Countries, IReadOnlyList<ViewShare> Devices, IReadOnlyList<ViewShare> Sources,
    IReadOnlyList<PostViews> TopPosts,
    long AllTime = 0);

public interface IViewStatsService
{
    Task<ViewStatsDto> GetAsync(Guid? postId, int days, CancellationToken ct = default);
}

/// <summary>Readers per day, and the coarse facts about them, for the admin portal. Everything here is a count.</summary>
public sealed class ViewStatsService : IViewStatsService
{
    public static readonly IReadOnlyList<int> Periods = [7, 30, 90];
    private const int TopPostsShown = 10;
    private const int SharesShown = 10;

    private readonly IReactionRepository _views;
    private readonly IPostRepository _posts;
    private readonly TimeProvider _time;

    public ViewStatsService(IReactionRepository views, IPostRepository posts, TimeProvider? time = null)
    {
        _views = views;
        _posts = posts;
        _time = time ?? TimeProvider.System;
    }

    public async Task<ViewStatsDto> GetAsync(Guid? postId, int days, CancellationToken ct = default)
    {
        if (!Periods.Contains(days)) throw new DomainException($"Choose a period of {string.Join(", ", Periods)} days.");
        var single = postId is { } id ? await _posts.GetByIdAsync(id, ct) ?? throw new NotFoundException("Post", id) : null;

        var today = DateOnly.FromDateTime(_time.GetUtcNow().UtcDateTime).DayNumber;
        var from = today - days + 1;
        var stats = await _views.GetStatsAsync(postId, from, ct);

        var daily = Enumerable.Range(from, days).Select(day =>
        {
            var rows = stats.Where(s => s.Day == day).ToList();
            return new DayViews(DateOnly.FromDayNumber(day).ToString("yyyy-MM-dd"), rows.Sum(s => s.Count), rows.Where(s => s.Returning).Sum(s => s.Count));
        }).ToList();

        static IReadOnlyList<ViewShare> Shares(IEnumerable<(string Key, int Count)> rows) =>
            rows.GroupBy(r => r.Key).Select(g => new ViewShare(g.Key, g.Sum(r => r.Count)))
                .OrderByDescending(s => s.Views).ThenBy(s => s.Key).Take(SharesShown).ToList();

        var top = new List<PostViews>();
        if (postId is null)
        {
            foreach (var group in stats.GroupBy(s => s.PostId).Select(g => (Id: g.Key, Views: g.Sum(s => s.Count))).OrderByDescending(g => g.Views).Take(TopPostsShown))
            {
                var post = await _posts.GetByIdAsync(group.Id, ct);
                if (post is not null) top.Add(new PostViews(post.Id, post.Title, post.Slug, group.Views));
            }
        }

        return new ViewStatsDto(
            days, stats.Sum(s => s.Count), stats.Where(s => s.Returning).Sum(s => s.Count), daily,
            Shares(stats.Select(s => (s.Country, s.Count))),
            Shares(stats.Select(s => (s.Device, s.Count))),
            Shares(stats.Select(s => (s.Source, s.Count))),
            top,
            single?.ViewCount ?? await _views.GetAllTimeViewsAsync(ct));
    }
}
