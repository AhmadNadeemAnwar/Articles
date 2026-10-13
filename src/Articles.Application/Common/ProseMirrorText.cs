using System.Text;
using System.Text.Json;
using Articles.Domain.Exceptions;

namespace Articles.Application.Common;

/// <summary>
/// Flattens a TipTap/ProseMirror document into plain text on the server.
/// The client never supplies the plain text: it feeds search, excerpts and reading time, so
/// trusting the browser for it would let a caller poison search results.
/// </summary>
public static class ProseMirrorText
{
    /// <summary>Block-level node types that should produce a paragraph break in the flattened output.</summary>
    private static readonly HashSet<string> BlockTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "paragraph", "heading", "blockquote", "codeBlock", "listItem", "horizontalRule",
        PostContent.TextSection
    };

    public static string Extract(string contentJson)
    {
        if (string.IsNullOrWhiteSpace(contentJson)) return string.Empty;

        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(contentJson);
        }
        catch (JsonException)
        {
            throw new DomainException("Post content must be a valid editor document.");
        }

        using (document)
        {
            var builder = new StringBuilder();
            Walk(document.RootElement, builder);
            return CollapseWhitespace(builder.ToString());
        }
    }

    private static void Walk(JsonElement element, StringBuilder builder)
    {
        if (element.ValueKind != JsonValueKind.Object) return;

        var type = element.TryGetProperty("type", out var typeElement) && typeElement.ValueKind == JsonValueKind.String
            ? typeElement.GetString()
            : null;

        if (type == "text" && element.TryGetProperty("text", out var textElement))
        {
            builder.Append(textElement.GetString());
            return;
        }

        // Earlier versions kept beside the newest improved text are not part of the post: not searched, shown or counted.
        if (type == "sections" && element.TryGetProperty("content", out var all) && all.ValueKind == JsonValueKind.Array)
        {
            var hidden = PostContent.HiddenSections(all);
            var position = 0;
            foreach (var section in all.EnumerateArray())
            {
                if (!hidden.Contains(position++)) Walk(section, builder);
            }
            return;
        }

        // Images contribute their alt text so illustrated posts stay findable.
        if (type == "image" && element.TryGetProperty("attrs", out var attrs)
            && attrs.TryGetProperty("alt", out var alt) && alt.ValueKind == JsonValueKind.String)
        {
            builder.Append(alt.GetString()).Append(' ');
            return;
        }

        // An image section is found by its description and caption.
        if (type == PostContent.ImageSection)
        {
            if (element.TryGetProperty("attrs", out var image) && image.ValueKind == JsonValueKind.Object)
            {
                AppendText(image, "alt", builder);
                AppendText(image, "caption", builder);
            }
            return;
        }

        // A reference section is found by the titles of its sources; their links are not prose.
        if (type == PostContent.ReferencesSection)
        {
            if (element.TryGetProperty("attrs", out var references) && references.ValueKind == JsonValueKind.Object
                && references.TryGetProperty("items", out var items) && items.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in items.EnumerateArray()) AppendText(item, "title", builder);
            }
            return;
        }

        if (element.TryGetProperty("content", out var content) && content.ValueKind == JsonValueKind.Array)
        {
            foreach (var child in content.EnumerateArray())
            {
                Walk(child, builder);
            }
        }

        if (type is not null && BlockTypes.Contains(type)) builder.Append('\n');
    }

    private static void AppendText(JsonElement element, string name, StringBuilder builder)
    {
        if (element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out var value)
            && value.ValueKind == JsonValueKind.String)
        {
            builder.Append(value.GetString()).Append('\n');
        }
    }

    private static string CollapseWhitespace(string value)
    {
        var builder = new StringBuilder(value.Length);
        var lastWasSpace = false;

        foreach (var ch in value)
        {
            if (char.IsWhiteSpace(ch))
            {
                if (!lastWasSpace && builder.Length > 0) builder.Append(' ');
                lastWasSpace = true;
            }
            else
            {
                builder.Append(ch);
                lastWasSpace = false;
            }
        }

        return builder.ToString().Trim();
    }
}
