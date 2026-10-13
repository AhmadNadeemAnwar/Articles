using System.Text.RegularExpressions;
using Articles.Application.Admin;
using Articles.Domain.Exceptions;

namespace Articles.Application.Posts;

/// <summary>A language model that continues a request. Implementations throw a <see cref="DomainException"/> with a plain message on failure.</summary>
public interface ITextModel
{
    /// <summary>False until the AI service is set up, so the editor can hide the feature instead of failing.</summary>
    bool IsEnabled { get; }

    Task<string> CompleteAsync(string instruction, string request, int maxTokens, double temperature, CancellationToken ct = default);
}

public sealed record ImproveTextRequest(IReadOnlyList<string>? Units);

/// <param name="Units">The improved lines, in the same order and the same number as were sent.</param>
/// <param name="Changed">How many of them are different from what was sent.</param>
public sealed record ImprovedTextDto(IReadOnlyList<string> Units, int Changed, int UsedToday, int DailyLimit);

public sealed record TextImprovementStatus(bool Enabled, int UsedToday, int DailyLimit);

public interface ITextImprovementService
{
    Task<TextImprovementStatus> GetStatusAsync(CancellationToken ct = default);
    Task<ImprovedTextDto> ImproveAsync(string admin, ImproveTextRequest request, CancellationToken ct = default);
}

/// <summary>
/// "Improve text with AI". The editor sends a section as numbered lines (one per paragraph, heading or list item, with
/// bold, italic, links and code written as simple markup) and gets the same lines back, reworded. The structure
/// is never the AI's to change: the number of lines must match, and links must keep their addresses. A line the
/// AI mangles is kept as the writer wrote it. The AI is free and limited, so it is capped here as well: a hard number of
/// goes a day (one per chunk of text), counted from the history in the database.
/// </summary>
public sealed partial class TextImprovementService : ITextImprovementService
{
    public const int DailyLimit = 20;
    public const int MaxUnits = 300;
    public const int MaxUnitLength = 3000;
    public const int MaxCharacters = 15_000;

    /// <summary>One go covers about this much text, which leaves room for the model to write the same again within its limit.</summary>
    public const int ChunkCharacters = 5000;
    public const int ChunkUnits = 40;
    public const int MaxAttempts = 2;

    private const string LineBreak = "⏎";

    public const string Instruction = """
        You are a careful, skilled editor improving an author's writing. You receive numbered lines, each one a paragraph, heading or list item, and return the SAME lines.

        For every line:
        - Fix spelling, grammar and punctuation.
        - Improve how the sentences connect and flow: use clearer, more meaningful words and fitting linking words (such as because, however, so, therefore, for example) so each idea leads naturally to the next.
        - Keep the author's meaning, voice, facts, numbers, names and quotations exactly. Never add new claims or opinions, and never remove an idea.
        - Keep the same language: English stays English, Urdu stays Urdu.
        - Keep the length about the same. Do not merge, split, add or drop lines.
        - If a line is already good, return it unchanged.

        Markup inside lines must be kept exactly where the same words are: **bold**, _italic_, ~~strikethrough~~, `code`, [link text](address). Never change, remove or invent an address in brackets. The symbol ⏎ marks a line break inside a line: keep it.

        Reply with ONLY the lines, each starting with its number in square brackets exactly as received, like:
        [1] improved text
        [2] improved text
        Nothing else: no introduction, no notes.
        """;

    private static readonly SemaphoreSlim Gate = new(1, 1);

    private readonly ITextModel _model;
    private readonly IActivityLog _activity;
    private readonly TimeProvider _time;

    public TextImprovementService(ITextModel model, IActivityLog activity, TimeProvider? time = null)
    {
        _model = model;
        _activity = activity;
        _time = time ?? TimeProvider.System;
    }

    private DateTimeOffset StartOfToday()
    {
        var now = _time.GetUtcNow();
        return new DateTimeOffset(now.Year, now.Month, now.Day, 0, 0, 0, TimeSpan.Zero);
    }

    public async Task<TextImprovementStatus> GetStatusAsync(CancellationToken ct = default) =>
        new(_model.IsEnabled, _model.IsEnabled ? await _activity.CountSinceAsync(Activity.ImprovedText, StartOfToday(), ct) : 0, DailyLimit);

    public async Task<ImprovedTextDto> ImproveAsync(string admin, ImproveTextRequest request, CancellationToken ct = default)
    {
        if (!_model.IsEnabled)
            throw new DomainException("The AI is not set up yet. It needs a free Gemini key, or the Cloudflare settings used for pictures. See ADMIN.md.");

        var units = request.Units ?? [];
        if (units.Count == 0 || units.All(string.IsNullOrWhiteSpace)) throw new DomainException("There is no text to improve yet.");
        if (units.Count > MaxUnits || units.Any(u => u.Length > MaxUnitLength) || units.Sum(u => u.Length) > MaxCharacters)
            throw new DomainException("This section is too long to improve in one go. Split it into two sections and improve each.");

        // The editor sends a long section in pieces, one request per piece, so each request stays quick and costs exactly one go.
        if (Chunk(units).Count != 1)
            throw new DomainException($"Send about {ChunkCharacters:N0} characters at a time. The editor does this for you.");

        await Gate.WaitAsync(ct);
        try
        {
            var used = await _activity.CountSinceAsync(Activity.ImprovedText, StartOfToday(), ct);
            if (used >= DailyLimit)
                throw new DomainException($"You have used all {DailyLimit} AI goes allowed today. This keeps the service free. The count resets at midnight UTC.");

            // Counted once the provider has been asked, whether or not the answer turns out to be usable.
            await _activity.RecordAsync(admin, Activity.ImprovedText, $"{units.Count} lines", ct);
            var result = await ImproveChunkAsync(units, ct);

            var changed = result.Where((text, i) => text != units[i]).Count();
            return new ImprovedTextDto(result, changed, used + 1, DailyLimit);
        }
        finally
        {
            Gate.Release();
        }
    }

    /// <summary>Groups line positions so that no group is more than one go's worth of text.</summary>
    public static List<List<int>> Chunk(IReadOnlyList<string> units)
    {
        var chunks = new List<List<int>>();
        var current = new List<int>();
        var size = 0;
        for (var i = 0; i < units.Count; i++)
        {
            if (current.Count > 0 && (size + units[i].Length > ChunkCharacters || current.Count >= ChunkUnits))
            {
                chunks.Add(current);
                current = [];
                size = 0;
            }
            current.Add(i);
            size += units[i].Length;
        }
        if (current.Count > 0) chunks.Add(current);
        return chunks;
    }

    private async Task<IReadOnlyList<string>> ImproveChunkAsync(IReadOnlyList<string> lines, CancellationToken ct)
    {
        var request = string.Join('\n', lines.Select((line, i) => $"[{i + 1}] {line.Replace("\r\n", "\n").Replace('\n', LineBreak[0])}"));
        var characters = lines.Sum(l => l.Length);
        // Enough for the same text to be written again, within what the model can hold.
        var maxTokens = Math.Clamp(characters / 2 + 300, 300, 3500);

        string[]? best = null;
        var bestCount = -1;
        for (var attempt = 1; attempt <= MaxAttempts; attempt++)
        {
            var reply = await _model.CompleteAsync(Instruction, request, maxTokens, 0.4, ct);
            var parsed = ParseLines(reply, lines.Count);

            var accepted = 0;
            var candidate = new string[lines.Count];
            for (var i = 0; i < lines.Count; i++)
            {
                if (parsed.TryGetValue(i + 1, out var text) && Acceptable(lines[i], text)) { candidate[i] = text; accepted++; }
                else candidate[i] = lines[i];
            }

            if (accepted > bestCount) { best = candidate; bestCount = accepted; }
            if (accepted == lines.Count) break;
        }

        if (bestCount <= 0 && lines.Any(l => !string.IsNullOrWhiteSpace(l)))
            throw new DomainException("The AI did not send back text that could be used. Try again.");

        return best!;
    }

    /// <summary>Reads "[n] text" lines from a model's reply, ignoring anything else it said.</summary>
    public static Dictionary<int, string> ParseLines(string? reply, int count)
    {
        var found = new Dictionary<int, string>();
        foreach (var raw in (reply ?? string.Empty).Replace("\r\n", "\n").Split('\n'))
        {
            var match = LineNumber().Match(raw.Trim());
            if (!match.Success || !int.TryParse(match.Groups[1].Value, out var number) || number < 1 || number > count) continue;
            found.TryAdd(number, match.Groups[2].Value.Replace(LineBreak, "\n").Trim());
        }
        return found;
    }

    /// <summary>
    /// A line is taken only if it still looks like the same piece of writing: not empty (unless it was), about the same
    /// length, and with every link address it had and no new one.
    /// </summary>
    public static bool Acceptable(string original, string improved)
    {
        if (string.IsNullOrWhiteSpace(original)) return string.IsNullOrWhiteSpace(improved);
        if (string.IsNullOrWhiteSpace(improved)) return false;

        if (original.Length >= 20 && (improved.Length < original.Length * 0.5 || improved.Length > original.Length * 2)) return false;

        var before = Addresses(original);
        var after = Addresses(improved);
        return before.SetEquals(after);
    }

    private static HashSet<string> Addresses(string text) =>
        LinkAddress().Matches(text).Select(m => m.Groups[1].Value).ToHashSet(StringComparer.Ordinal);

    [GeneratedRegex(@"^\[(\d{1,4})\]\s*(.*)$", RegexOptions.Singleline)]
    private static partial Regex LineNumber();

    [GeneratedRegex(@"\]\(([^)\s]+)\)")]
    private static partial Regex LinkAddress();
}
