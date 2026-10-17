namespace Articles.Domain.Entities;

/// <summary>
/// How many readers opened a post on one day, split by a few coarse facts: country, kind of device, the site they came
/// from, and whether they had read it on an earlier day. Only counts are kept: nothing here leads back to a reader.
/// </summary>
public class PostViewStat
{
    public const int CountryLength = 2;
    public const int MaxSourceLength = 100;

    public Guid PostId { get; private set; }

    /// <summary>Days since 0001-01-01 (UTC), as <see cref="DateOnly.DayNumber"/>, like <see cref="PostView.Day"/>.</summary>
    public int Day { get; private set; }

    /// <summary>Two-letter country code, or empty when it is not known.</summary>
    public string Country { get; private set; } = string.Empty;

    /// <summary>"phone", "tablet" or "computer".</summary>
    public string Device { get; private set; } = string.Empty;

    /// <summary>The site the reader came from, such as "google.com", or empty when they came straight to it.</summary>
    public string Source { get; private set; } = string.Empty;

    /// <summary>True when this reader had opened the post on an earlier day.</summary>
    public bool Returning { get; private set; }

    public int Count { get; private set; }

    private PostViewStat() { }

    public PostViewStat(Guid postId, int day, string country, string device, string source, bool returning)
    {
        PostId = postId;
        Day = day;
        Country = country;
        Device = device;
        Source = source;
        Returning = returning;
        Count = 1;
    }

    public void Add() => Count++;
}
