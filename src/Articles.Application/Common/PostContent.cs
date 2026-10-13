using System.Text.Json;
using Articles.Domain.Common;
using Articles.Domain.Exceptions;

namespace Articles.Application.Common;

/// <summary>
/// Checks the shape of a post body before it is stored. A body is either the original single
/// editor document (type "doc"), or a list of sections (type "sections") that the author can
/// reorder: text, an uploaded image, or a list of sources.
///
/// The public site renders only what it recognises, so this is not the only line of defence, but
/// rejecting bad input here keeps junk and unsafe links out of the database in the first place.
/// </summary>
public static class PostContent
{
    public const string TextSection = "textSection";
    public const string ImageSection = "imageSection";
    public const string ReferencesSection = "referencesSection";

    /// <summary>
    /// A text section can carry a "textState", so that the writer's earlier wording is kept beside the AI's improved version:
    /// "Original" is what the writer wrote, "Improved" is the first improvement, and each later improvement is
    /// "Improved_" and the time it was made (for example "Improved_2026-10-14T10:30:00Z"). They sit side by side, oldest first.
    /// Readers see only the newest of them. The older ones stay in the body, but are never shown, searched or counted in
    /// reading time. A section with no state is ordinary text.
    /// </summary>
    public const string TextStateOriginal = "Original";
    public const string TextStateImproved = "Improved";
    public const string TextStateImprovedPrefix = "Improved_";

    private static readonly System.Text.RegularExpressions.Regex ImprovedAt =
        new(@"^Improved_\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}(\.\d{1,7})?Z$", System.Text.RegularExpressions.RegexOptions.CultureInvariant);

    /// <summary>"Improved", or "Improved_" and a time: one of the AI's versions rather than the writer's original.</summary>
    public static bool IsImprovedState(string? state) => state == TextStateImproved || (state is not null && ImprovedAt.IsMatch(state));

    public const int MaxLength = 1_000_000;
    /// <summary>The editor allows 100 sections; each earlier version it keeps is another entry here.</summary>
    public const int MaxSections = 300;
    public const int MaxReferences = 50;
    public const int MaxCaptionLength = 300;
    public const int MaxReferenceTitleLength = 200;
    public const int MaxUrlLength = 2048;

    /// <summary>Far larger than any picture the editor produces (it shrinks them to 1600), but small enough to be a real size.</summary>
    public const int MaxImageDimension = 20_000;

    private const string NotADocument = "Post content must be a valid editor document.";

    public static void Validate(string contentJson)
    {
        if (string.IsNullOrWhiteSpace(contentJson)) throw new DomainException("Post content is required.");
        if (contentJson.Length > MaxLength) throw new DomainException("This post is too long to save.");

        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(contentJson);
        }
        catch (JsonException)
        {
            throw new DomainException(NotADocument);
        }

        using (document)
        {
            var root = document.RootElement;
            switch (TypeOf(root))
            {
                case "doc":
                    return;
                case "sections":
                    ValidateSections(root);
                    return;
                default:
                    throw new DomainException(NotADocument);
            }
        }
    }

    private static void ValidateSections(JsonElement root)
    {
        if (!root.TryGetProperty("content", out var sections) || sections.ValueKind != JsonValueKind.Array)
            throw new DomainException(NotADocument);
        if (sections.GetArrayLength() > MaxSections)
            throw new DomainException($"A post can have at most {MaxSections} sections.");

        string? previous = null;
        foreach (var section in sections.EnumerateArray())
        {
            var type = TypeOf(section);
            var state = type == TextSection ? ValidateTextState(section) : null;

            // The writer's original is only ever kept beside the version that replaced it, oldest first.
            if (previous == TextStateOriginal && !IsImprovedState(state))
                throw new DomainException("A kept original text must be followed by the improved text that replaced it.");
            if (state == TextStateImproved && previous != TextStateOriginal)
                throw new DomainException("An improved text must come straight after the original it replaced.");
            if (state is not null && state != TextStateOriginal && state != TextStateImproved && !IsImprovedState(previous) && previous != TextStateOriginal)
                throw new DomainException("An improved text must come straight after the version it replaced.");
            previous = state;

            switch (type)
            {
                case TextSection:
                    break;
                case ImageSection:
                    ValidateImage(section);
                    break;
                case ReferencesSection:
                    ValidateReferences(section);
                    break;
                default:
                    throw new DomainException("A post can only contain text, image and reference sections.");
            }
        }

        if (previous == TextStateOriginal) throw new DomainException("A kept original text must be followed by the improved text that replaced it.");
    }

    /// <summary>The text section's state, or null for an ordinary one. Anything but the known states is refused.</summary>
    private static string? ValidateTextState(JsonElement section)
    {
        if (!section.TryGetProperty("textState", out var value) || value.ValueKind == JsonValueKind.Null) return null;

        var state = value.ValueKind == JsonValueKind.String ? value.GetString() : null;
        return state == TextStateOriginal || IsImprovedState(state)
            ? state
            : throw new DomainException("A text section can only be marked Original, Improved, or Improved_ and the time it was made.");
    }

    /// <summary>The positions of the sections readers do not see: every Original, and every improved text that a newer one has replaced.</summary>
    public static HashSet<int> HiddenSections(JsonElement sections)
    {
        var states = sections.ValueKind == JsonValueKind.Array
            ? sections.EnumerateArray().Select(s => TypeOf(s) == TextSection ? Text(s, "textState") : null).ToList()
            : [];

        var hidden = new HashSet<int>();
        for (var i = 0; i < states.Count; i++)
        {
            if (states[i] == TextStateOriginal) hidden.Add(i);
            else if (IsImprovedState(states[i]) && i + 1 < states.Count && IsImprovedState(states[i + 1])) hidden.Add(i);
        }
        return hidden;
    }

    private static void ValidateImage(JsonElement section)
    {
        var attrs = Attrs(section);
        if (!Guid.TryParse(Text(attrs, "imageId"), out _))
            throw new DomainException("An image section has no picture. Upload one or remove the section.");

        // The picture's size is optional (older posts have none). When present it lets a page keep the
        // picture's space free while it loads, so it has to be a believable size, and both or neither.
        var width = Dimension(attrs, "width");
        var height = Dimension(attrs, "height");
        if (width == Invalid || height == Invalid || (width is null) != (height is null))
            throw new DomainException("An image section has a size that is not valid. Upload the picture again.");

        if ((Text(attrs, "alt")?.Length ?? 0) > MaxCaptionLength || (Text(attrs, "caption")?.Length ?? 0) > MaxCaptionLength)
            throw new DomainException($"Image descriptions and captions can be at most {MaxCaptionLength} characters.");
    }

    private static void ValidateReferences(JsonElement section)
    {
        var attrs = Attrs(section);
        if (attrs.ValueKind != JsonValueKind.Object || !attrs.TryGetProperty("items", out var items) || items.ValueKind != JsonValueKind.Array)
            throw new DomainException("A reference section must be a list of sources.");
        if (items.GetArrayLength() > MaxReferences)
            throw new DomainException($"A reference section can list at most {MaxReferences} sources.");

        foreach (var item in items.EnumerateArray())
        {
            var title = Text(item, "title");
            var url = Text(item, "url");

            if (string.IsNullOrWhiteSpace(title)) throw new DomainException("Every reference needs a title.");
            if (title.Length > MaxReferenceTitleLength)
                throw new DomainException($"A reference title can be at most {MaxReferenceTitleLength} characters.");

            // The link is optional (a book has none), but when present it must be a real web address.
            if (!string.IsNullOrWhiteSpace(url) && (url.Length > MaxUrlLength || !UrlRules.IsHttpOrHttps(url)))
                throw new DomainException($"The link for \"{title}\" must start with http:// or https://.");
        }
    }

    private const int Invalid = -1;

    /// <summary>Null when absent, <see cref="Invalid"/> when present but not a whole number in range.</summary>
    private static int? Dimension(JsonElement attrs, string name)
    {
        if (attrs.ValueKind != JsonValueKind.Object || !attrs.TryGetProperty(name, out var value) || value.ValueKind == JsonValueKind.Null) return null;
        return value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var number) && number is >= 1 and <= MaxImageDimension ? number : Invalid;
    }

    private static string? TypeOf(JsonElement element) =>
        element.ValueKind == JsonValueKind.Object ? Text(element, "type") : null;

    private static JsonElement Attrs(JsonElement section) =>
        section.TryGetProperty("attrs", out var attrs) ? attrs : default;

    private static string? Text(JsonElement element, string name) =>
        element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;
}
