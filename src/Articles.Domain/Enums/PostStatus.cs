namespace Articles.Domain.Enums;

/// <summary>The three states a post can be in. Any state can be changed to any other.</summary>
public enum PostStatus
{
    /// <summary>Still being written. Visible only to its author.</summary>
    Draft = 0,
    /// <summary>Publicly listed, indexed, and searchable.</summary>
    Published = 1,
    /// <summary>
    /// Switched off. Hidden from everyone but its author, and it keeps its address for when it is
    /// published again. Stored as 2, the value older versions called "Unlisted" and never used.
    /// </summary>
    Inactive = 2
}
