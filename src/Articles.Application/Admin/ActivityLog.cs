using Articles.Domain.Common;
using Articles.Domain.Entities;

namespace Articles.Application.Admin;

public sealed record ActivityDto(Guid Id, DateTimeOffset At, string Actor, string Action, string Subject);

/// <summary>
/// The admin portal's history. Recording is deliberately forgiving: a failure to write a history
/// line is logged and swallowed, because it must never undo or fail the action it describes.
/// </summary>
public interface IActivityLog
{
    /// <summary>Call after the action itself has been saved.</summary>
    Task RecordAsync(string actor, string action, string? subject = null, CancellationToken ct = default);

    Task<PagedResult<ActivityDto>> GetPageAsync(int pageNumber, int pageSize, CancellationToken ct = default);

    /// <summary>How many times this action has been recorded since the given moment.</summary>
    Task<int> CountSinceAsync(string action, DateTimeOffset since, CancellationToken ct = default);
}

/// <summary>The wording used in the history, kept in one place so the same action always reads the same.</summary>
public static class Activity
{
    /// <summary>How long history is kept. Older lines are removed as new sign-ins are recorded.</summary>
    public static readonly TimeSpan Retention = TimeSpan.FromDays(180);

    public const string SignedIn = "Signed in";
    public const string FailedSignIn = "Failed sign-in attempt";
    public const string ChangedPostStatus = "Changed post status";
    public const string MovedPostCategory = "Moved post to a category";
    public const string DeletedPost = "Deleted post";
    public const string RemovedComment = "Removed comment";
    public const string RenamedTopic = "Renamed topic";
    public const string MergedTopic = "Merged topic";
    public const string DeletedTopic = "Deleted topic";
    public const string SavedPortfolioEntry = "Saved portfolio entry";
    public const string DeletedPortfolioEntry = "Deleted portfolio entry";
    public const string ExportedPosts = "Exported all posts";
    public const string ChangedTheme = "Changed the site theme";
    public const string AddedCategory = "Added category";
    public const string RenamedCategory = "Renamed category";
    public const string DeletedCategory = "Deleted category";
    public const string NotifiedSubscribers = "Emailed subscribers about a post";
    public const string RemovedSubscriber = "Removed a subscriber";
    public const string GeneratedImage = "Generated a picture";

    public static ActivityDto ToDto(this ActivityEntry entry) => new(entry.Id, entry.At, entry.Actor, entry.Action, entry.Subject);
}

/// <summary>Used where no history is wanted, such as unit tests of unrelated behaviour.</summary>
public sealed class NoActivityLog : IActivityLog
{
    public static readonly NoActivityLog Instance = new();

    public Task RecordAsync(string actor, string action, string? subject = null, CancellationToken ct = default) => Task.CompletedTask;

    public Task<PagedResult<ActivityDto>> GetPageAsync(int pageNumber, int pageSize, CancellationToken ct = default) =>
        Task.FromResult(new PagedResult<ActivityDto>([], pageNumber, pageSize, 0));

    public Task<int> CountSinceAsync(string action, DateTimeOffset since, CancellationToken ct = default) => Task.FromResult(0);
}
