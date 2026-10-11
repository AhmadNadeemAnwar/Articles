namespace Articles.Application.Grammar;

/// <summary>One thing the checker suggests changing. Offsets count UTF-16 characters, as the browser does.</summary>
public sealed record GrammarMatch(int Offset, int Length, string Message, string Category, IReadOnlyList<string> Replacements);

public sealed record GrammarResult(IReadOnlyList<GrammarMatch> Matches);

/// <summary>Finds spelling, grammar and style problems in plain text.</summary>
public interface IGrammarChecker
{
    /// <summary>The longest text one check accepts. The free service refuses anything larger.</summary>
    const int MaxCharacters = 15_000;

    /// <summary>Throws a <see cref="Articles.Domain.Exceptions.DomainException"/> with a plain message when the service cannot be reached.</summary>
    Task<GrammarResult> CheckAsync(string text, CancellationToken ct = default);
}
