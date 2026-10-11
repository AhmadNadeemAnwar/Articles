using Articles.Application.Common;
using Articles.Domain.Entities;
using Articles.Domain.Enums;
using Articles.Domain.Exceptions;
using Articles.Domain.Interfaces;

namespace Articles.Application.Posts;

/// <summary>
/// The one place a post moves between Draft, Published and Inactive, shared by authors and admins
/// so the bookkeeping (the address on first publish, and each topic's count of live posts) cannot
/// drift between the two.
/// </summary>
internal static class PostStatusChange
{
    private const string Allowed = "Status must be Draft, Published or Inactive.";

    public static PostStatus Parse(string? value) =>
        // Names only: Enum.TryParse would also accept "7" or "1".
        !string.IsNullOrWhiteSpace(value) && !char.IsDigit(value.Trim()[0])
        && Enum.TryParse<PostStatus>(value.Trim(), ignoreCase: true, out var status) && Enum.IsDefined(status)
            ? status
            : throw new DomainException(Allowed);

    public static async Task ApplyAsync(Post post, PostStatus target, IPostRepository posts, CancellationToken ct)
    {
        var wasLive = post.Status == PostStatus.Published;

        switch (target)
        {
            case PostStatus.Published:
                post.Publish(post.Slug ?? await ResolveSlugAsync(post.Title, posts, ct));
                break;
            case PostStatus.Inactive:
                post.Deactivate();
                break;
            case PostStatus.Draft:
                post.MoveToDraft();
                break;
            default:
                throw new DomainException(Allowed);
        }

        var isLive = post.Status == PostStatus.Published;
        if (wasLive == isLive) return;

        foreach (var postTag in post.PostTags)
        {
            if (isLive) postTag.Tag?.IncrementPostCount();
            else postTag.Tag?.DecrementPostCount();
        }
    }

    /// <summary>
    /// The address for a post being published for the first time: its title, then a short code.
    /// Only ever called on first publish; an existing address is never recomputed, which is why
    /// posts published before codes were introduced keep their plain addresses.
    /// </summary>
    private static async Task<string> ResolveSlugAsync(string title, IPostRepository posts, CancellationToken ct)
    {
        var baseSlug = SlugGenerator.Generate(title);
        if (string.IsNullOrEmpty(baseSlug)) baseSlug = "post";

        // A clash needs the same title and the same code out of ~28 million, so one try is almost always enough.
        for (var attempt = 0; attempt < 5; attempt++)
        {
            var candidate = SlugGenerator.WithCode(baseSlug);
            if (!await posts.SlugExistsAsync(candidate, ct)) return candidate;
        }

        return SlugGenerator.WithSuffix(baseSlug);
    }
}
