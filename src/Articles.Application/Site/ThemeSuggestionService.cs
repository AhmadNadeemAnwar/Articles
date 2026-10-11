using System.Text.Json;
using Articles.Application.Admin;
using Articles.Domain.Exceptions;
using Articles.Domain.Interfaces;

namespace Articles.Application.Site;

/// <summary>Asks a language model a question and returns what it wrote. Implementations throw a <see cref="DomainException"/> with a plain message on failure.</summary>
public interface IThemeAi
{
    /// <summary>False until the AI service is set up, so the admin portal can hide the button instead of failing.</summary>
    bool IsEnabled { get; }

    Task<string> AskAsync(string instruction, string request, CancellationToken ct = default);
}

public sealed record ThemeAiStatus(bool Enabled, int UsedToday, int DailyLimit);

/// <param name="Name">What the AI called its theme, for the toast; may be empty.</param>
public sealed record ThemeSuggestion(ThemeColorsDto Colors, string Name, int UsedToday, int DailyLimit);

public interface IThemeSuggestionService
{
    Task<ThemeAiStatus> GetStatusAsync(CancellationToken ct = default);

    /// <summary>Three colours chosen by the AI. Nothing is saved: the owner looks at them first and then keeps them or not.</summary>
    Task<ThemeSuggestion> SuggestAsync(string admin, CancellationToken ct = default);
}

/// <summary>
/// "Amaze with AI": the AI proposes a header, a links-and-buttons and a page colour. Whatever it says is
/// checked like colours typed in by hand (plain #rrggbb, a background text can be read on), and asked
/// again a couple of times if not, so a bad answer never reaches the pickers. It runs on a free
/// allowance, so it is capped here as well as by the provider, counted from the history in the database.
/// </summary>
public sealed class ThemeSuggestionService : IThemeSuggestionService
{
    public const int DailyLimit = 20;
    public const int Attempts = 3;

    private const string Instruction =
        "You are a web designer choosing a colour theme for a personal blog of articles. " +
        "Reply with ONLY a JSON object, no other text, in exactly this shape: " +
        "{\"name\":\"two or three word theme name\",\"header\":\"#rrggbb\",\"accent\":\"#rrggbb\",\"background\":\"#rrggbb\"}. " +
        "header is the bar across the top and may be deep or rich. accent is for links and buttons: a vivid, confident colour that stands out. " +
        "background is the page behind the text and must be very light (like an off-white with a hint of colour) or very dark, never a mid-tone, so text is easy to read. " +
        "The three must look good together. Be creative and distinctive, not generic.";

    private static readonly SemaphoreSlim Gate = new(1, 1);

    private readonly IThemeAi _ai;
    private readonly ISiteRepository _site;
    private readonly IActivityLog _activity;
    private readonly TimeProvider _time;

    public ThemeSuggestionService(IThemeAi ai, ISiteRepository site, IActivityLog activity, TimeProvider? time = null)
    {
        _ai = ai;
        _site = site;
        _activity = activity;
        _time = time ?? TimeProvider.System;
    }

    private DateTimeOffset StartOfToday()
    {
        var now = _time.GetUtcNow();
        return new DateTimeOffset(now.Year, now.Month, now.Day, 0, 0, 0, TimeSpan.Zero);
    }

    public async Task<ThemeAiStatus> GetStatusAsync(CancellationToken ct = default) =>
        new(_ai.IsEnabled, _ai.IsEnabled ? await _activity.CountSinceAsync(Activity.AskedAiForColours, StartOfToday(), ct) : 0, DailyLimit);

    public async Task<ThemeSuggestion> SuggestAsync(string admin, CancellationToken ct = default)
    {
        if (!_ai.IsEnabled)
            throw new DomainException("The AI is not set up yet. It uses the same Cloudflare settings as picture generation. See ADMIN.md.");

        await Gate.WaitAsync(ct);
        try
        {
            var used = await _activity.CountSinceAsync(Activity.AskedAiForColours, StartOfToday(), ct);
            if (used >= DailyLimit)
                throw new DomainException($"You have asked the AI {DailyLimit} times today. This keeps the service free. The count resets at midnight UTC.");

            var current = ThemeColors.Read(await _site.GetSettingAsync(ThemeColors.SettingKey, ct));
            var request = "Choose a fresh theme. " +
                $"Style seed: {Random.Shared.Next(1000, 9999)}. " +
                (current is null ? string.Empty : $"Do not repeat the current colours ({current.Main}, {current.Accent}, {current.Background}).");

            // Counted once the provider has been asked, whether or not the answer turns out to be usable.
            await _activity.RecordAsync(admin, Activity.AskedAiForColours, "theme colours", ct);

            for (var attempt = 1; attempt <= Attempts; attempt++)
            {
                var suggestion = Parse(await _ai.AskAsync(Instruction, request, ct));
                if (suggestion is not null) return suggestion with { UsedToday = used + 1, DailyLimit = DailyLimit };
            }

            throw new DomainException("The AI did not come up with colours that work. Try again.");
        }
        finally
        {
            Gate.Release();
        }
    }

    /// <summary>The colours in a model's reply, or null if there are none that pass the same rules as colours typed by hand.</summary>
    public static ThemeSuggestion? Parse(string? reply)
    {
        var text = reply ?? string.Empty;
        var start = text.IndexOf('{');
        var end = text.LastIndexOf('}');
        if (start < 0 || end <= start) return null;

        try
        {
            using var document = JsonDocument.Parse(text[start..(end + 1)]);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object) return null;

            string? Field(params string[] names)
            {
                foreach (var name in names)
                    foreach (var property in root.EnumerateObject())
                        if (string.Equals(property.Name, name, StringComparison.OrdinalIgnoreCase) && property.Value.ValueKind == JsonValueKind.String)
                            return property.Value.GetString();
                return null;
            }

            var colors = ThemeColors.Normalise(Field("header", "main"), Field("background"), Field("accent"));
            var name = (Field("name") ?? string.Empty).Trim();
            if (name.Length > 40) name = name[..40];
            // The name is only ever shown as text, but keep to plain characters in case it is not.
            name = new string(name.Where(c => char.IsLetterOrDigit(c) || c is ' ' or '-' or '\'' or '&').ToArray()).Trim();
            return new ThemeSuggestion(colors, name, 0, DailyLimit);
        }
        catch (Exception ex) when (ex is JsonException or DomainException)
        {
            return null;
        }
    }
}
