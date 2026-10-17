using Articles.Domain.Common;
using Articles.Domain.Exceptions;

namespace Articles.Domain.Entities;

/// <summary>
/// A reader's short, anonymous note after reacting to a post: what they liked (chosen from a few set words) and, if they
/// want, a suggestion in their own words. No name, email or account is kept with it.
/// </summary>
public class ReaderFeedback : BaseEntity
{
    public const int MaxTextLength = 500;

    /// <summary>The things a reader can say they liked, as stored. The site shows each in words of its own.</summary>
    public static readonly IReadOnlyList<string> LikedOptions = ["clear-writing", "useful", "made-me-think", "personal"];

    public Guid PostId { get; private set; }
    public Post Post { get; private set; } = null!;

    /// <summary>The chosen options, comma-separated, in the order of <see cref="LikedOptions"/>. Empty when none were picked.</summary>
    public string Liked { get; private set; } = string.Empty;
    public string Text { get; private set; } = string.Empty;

    private ReaderFeedback() { }

    public ReaderFeedback(Guid postId, IEnumerable<string>? liked, string? text)
    {
        var chosen = (liked ?? []).Select(l => l?.Trim().ToLowerInvariant() ?? string.Empty).Where(l => l.Length > 0).Distinct().ToList();
        if (chosen.Any(l => !LikedOptions.Contains(l))) throw new DomainException("That is not one of the choices.");

        // Line breaks are kept; control characters are not, and the note is only ever shown as plain text.
        var clean = new string((text ?? string.Empty).Replace("\r\n", "\n").Where(c => c == '\n' || !char.IsControl(c)).ToArray()).Trim();
        if (clean.Length > MaxTextLength) throw new DomainException($"Keep it under {MaxTextLength} characters.");
        if (chosen.Count == 0 && clean.Length == 0) throw new DomainException("Pick something you liked, or write a few words.");

        PostId = postId;
        Liked = string.Join(',', LikedOptions.Where(chosen.Contains));
        Text = clean;
    }

    public IReadOnlyList<string> LikedList => Liked.Length == 0 ? [] : Liked.Split(',');
}
