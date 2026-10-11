namespace Articles.Domain.Enums;

/// <summary>The ways a reader can respond to a post. Kept to two on purpose: more would split a small audience into tiny numbers.</summary>
public enum ReactionKind
{
    Clap = 0,
    Insightful = 1
}
