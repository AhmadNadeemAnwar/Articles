using System.Security.Cryptography;
using System.Text;
using Articles.Domain.Entities;
using Articles.Domain.Enums;
using Articles.Domain.Exceptions;
using Articles.Domain.Interfaces;

namespace Articles.Application.Engagement;

/// <summary>A post's reaction totals, and which of them this visitor has given.</summary>
public sealed record ReactionState(int ClapCount, int InsightfulCount, bool Clapped, bool MarkedInsightful);

public interface IReactionService
{
    /// <summary>Totals for a published post, plus this visitor's own reactions when they can be identified.</summary>
    Task<ReactionState> GetAsync(Guid postId, string? visitorId, CancellationToken ct = default);

    /// <summary>Gives the reaction if the visitor has not, takes it back if they have.</summary>
    Task<ReactionState> ToggleAsync(Guid postId, string? visitorId, string? kind, CancellationToken ct = default);

    /// <summary>Counts one read per visitor per post per day. Returns whether this call was counted.</summary>
    /// <param name="country">Two-letter country code Cloudflare gave the page, if any. Only the code is kept, never an address.</param>
    /// <param name="referrerHost">The site the reader came from, as the browser reported it. Only its bare name is kept.</param>
    Task<bool> RecordViewAsync(Guid postId, string? visitorId, string? userAgent, Guid? signedInUserId, CancellationToken ct = default,
        string? country = null, string? referrerHost = null);
}

public sealed class ReactionService : IReactionService
{
    private readonly IReactionRepository _reactions;
    private readonly IPostRepository _posts;
    private readonly TimeProvider _time;

    public ReactionService(IReactionRepository reactions, IPostRepository posts, TimeProvider? time = null)
    {
        _reactions = reactions;
        _posts = posts;
        _time = time ?? TimeProvider.System;
    }

    public async Task<ReactionState> GetAsync(Guid postId, string? visitorId, CancellationToken ct = default)
    {
        var post = await PublishedPostAsync(postId, ct);
        return await StateAsync(post, VisitorKey.From(visitorId), ct);
    }

    public async Task<ReactionState> ToggleAsync(Guid postId, string? visitorId, string? kind, CancellationToken ct = default)
    {
        var parsed = ParseKind(kind);
        var key = VisitorKey.From(visitorId)
            ?? throw new DomainException("Your reaction could not be saved from this browser.");
        var post = await PublishedPostAsync(postId, ct);

        var existing = await _reactions.GetAsync(postId, key, parsed, ct);
        if (existing is null)
        {
            await _reactions.AddAsync(new Reaction(postId, key, parsed), ct);
            post.AddReaction(parsed);
        }
        else
        {
            _reactions.Remove(existing);
            post.RemoveReaction(parsed);
        }

        if (!await _reactions.TrySaveAsync(ct))
        {
            // The same visitor's other click won the race. Nothing was saved here, so report what is stored.
            post = await PublishedPostAsync(postId, ct);
        }

        return await StateAsync(post, key, ct);
    }

    /// <summary>How long a scrambled reader id is kept per post, to tell a returning reader from a new one.</summary>
    public const int KeepViewsDays = 90;

    /// <summary>A reader arriving from one of these (or a subdomain) moved within the owner's own sites, so counts as direct.</summary>
    public static readonly IReadOnlyList<string> OwnSites = ["ahmadnadeem.dev", "localhost"];

    public async Task<bool> RecordViewAsync(Guid postId, string? visitorId, string? userAgent, Guid? signedInUserId, CancellationToken ct = default,
        string? country = null, string? referrerHost = null)
    {
        if (BotDetector.IsBot(userAgent)) return false;
        if (VisitorKey.From(visitorId) is not { } key) return false;

        var post = await _posts.GetByIdAsync(postId, ct);
        if (post is null || post.Status != PostStatus.Published) return false;

        // The writer checking their own post is not a reader.
        if (signedInUserId == post.AuthorId) return false;

        var today = DateOnly.FromDateTime(_time.GetUtcNow().UtcDateTime).DayNumber;
        if (await _reactions.HasViewAsync(postId, key, today, ct)) return false;

        await _reactions.AddViewAsync(new PostView(postId, key, today), ct);
        post.RegisterView();

        // The day's counts: only coarse facts, and nothing that leads back to this reader.
        var returning = await _reactions.HasViewBeforeAsync(postId, key, today, ct);
        var (place, device, source) = (ViewFacts.Country(country), ViewFacts.Device(userAgent), ViewFacts.Source(referrerHost, OwnSites));
        var stat = await _reactions.FindStatAsync(postId, today, place, device, source, returning, ct);
        if (stat is null) await _reactions.AddStatAsync(new PostViewStat(postId, today, place, device, source, returning), ct);
        else stat.Add();

        if (!await _reactions.TrySaveAsync(ct)) return false;

        await _reactions.DeleteViewsBeforeAsync(today - KeepViewsDays, ct);
        return true;
    }

    private async Task<Post> PublishedPostAsync(Guid postId, CancellationToken ct)
    {
        var post = await _posts.GetByIdAsync(postId, ct);
        // A draft or inactive post answers exactly like one that does not exist.
        return post is { Status: PostStatus.Published } ? post : throw new NotFoundException(nameof(Post), postId);
    }

    private async Task<ReactionState> StateAsync(Post post, string? visitorKey, CancellationToken ct)
    {
        var mine = visitorKey is null ? [] : await _reactions.GetKindsAsync(post.Id, visitorKey, ct);
        return new ReactionState(post.ClapCount, post.InsightfulCount, mine.Contains(ReactionKind.Clap), mine.Contains(ReactionKind.Insightful));
    }

    private static ReactionKind ParseKind(string? kind) =>
        // Names only: Enum.TryParse would also accept "0" or "7".
        !string.IsNullOrWhiteSpace(kind) && !char.IsDigit(kind.Trim()[0])
        && Enum.TryParse<ReactionKind>(kind.Trim(), ignoreCase: true, out var parsed) && Enum.IsDefined(parsed)
            ? parsed
            : throw new DomainException("A reaction must be clap or insightful.");
}

/// <summary>
/// Turns the random id a browser sends into the value that is stored. Only a well-formed id is
/// accepted, and only its hash is kept, so the database never holds anything a browser could be
/// matched to, and a caller cannot stuff arbitrary text into it.
/// </summary>
public static class VisitorKey
{
    public static string? From(string? visitorId)
    {
        if (!Guid.TryParse(visitorId?.Trim(), out var id) || id == Guid.Empty) return null;
        return Convert.ToHexStringLower(SHA256.HashData(Encoding.ASCII.GetBytes(id.ToString("D"))));
    }
}

/// <summary>
/// Recognises automated visitors by how they announce themselves. It only has to keep honest
/// crawlers and link-preview fetchers out of the view count; it is not a security control.
/// </summary>
public static class BotDetector
{
    /// <summary>Words that only automated visitors put in their User-Agent.</summary>
    private static readonly string[] Markers =
    [
        "bot", "crawl", "spider", "slurp", "preview", "fetch", "scrape", "monitor", "uptime", "pingdom",
        "lighthouse", "headless", "curl", "wget", "python", "httpclient", "okhttp", "go-http", "java/",
        "libwww", "facebookexternalhit", "whatsapp", "telegram", "discord", "slack", "embedly", "quora",
        "pinterest", "feed", "rss"
    ];

    public static bool IsBot(string? userAgent) =>
        string.IsNullOrWhiteSpace(userAgent)
        || Markers.Any(marker => userAgent.Contains(marker, StringComparison.OrdinalIgnoreCase));
}
