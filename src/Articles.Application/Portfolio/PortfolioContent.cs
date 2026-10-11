using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Articles.Domain.Exceptions;
using YamlDotNet.Core;
using YamlDotNet.Core.Events;
using YamlDotNet.RepresentationModel;

namespace Articles.Application.Portfolio;

public enum FieldKind { Text, Date, Flag, TextList, Url, Link, Impact }

/// <param name="Key">The front-matter key.</param>
/// <param name="Max">Longest text, or most list items, allowed.</param>
public sealed record FieldSpec(string Key, FieldKind Kind, bool Required = false, int Max = 200);

/// <summary>
/// The portfolio's content rules. These mirror <c>src/content.config.ts</c> in the Astro site: a file
/// that breaks them fails the site's build, so they are enforced here before anything is committed.
/// If the site's schema changes, change this to match.
/// </summary>
public static class PortfolioContent
{
    public const int MaxBodyLength = 200_000;

    private static readonly Regex SlugPattern = new("^[a-z0-9](?:[a-z0-9-]{0,78}[a-z0-9])?$", RegexOptions.Compiled);
    private static readonly Regex UnknownKeyPattern = new("^[A-Za-z][A-Za-z0-9_-]{0,40}$", RegexOptions.Compiled);
    private static readonly Regex NumberLike = new(@"^[-+]?(\d[\d_]*)(\.\d*)?([eE][-+]?\d+)?$|^0[xob][0-9a-fA-F_]+$|^[-+]?\.(inf|nan)$", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex DateLike = new(@"^\d{4}-\d{2}-\d{2}", RegexOptions.Compiled);
    private static readonly HashSet<string> Reserved = new(StringComparer.OrdinalIgnoreCase)
    {
        "null", "~", "true", "false", "yes", "no", "on", "off", "y", "n"
    };

    public static readonly IReadOnlyDictionary<string, IReadOnlyList<FieldSpec>> Collections =
        new Dictionary<string, IReadOnlyList<FieldSpec>>(StringComparer.Ordinal)
        {
            ["blog"] =
            [
                new("title", FieldKind.Text, Required: true, Max: 200),
                new("description", FieldKind.Text, Required: true, Max: 400),
                new("date", FieldKind.Date, Required: true),
                new("tags", FieldKind.TextList, Max: 10),
                new("draft", FieldKind.Flag)
            ],
            ["projects"] =
            [
                new("title", FieldKind.Text, Required: true, Max: 200),
                new("summary", FieldKind.Text, Required: true, Max: 500),
                new("date", FieldKind.Date, Required: true),
                new("tech", FieldKind.TextList, Max: 20),
                new("featured", FieldKind.Flag),
                new("impact", FieldKind.Impact),
                new("repo", FieldKind.Url),
                new("demo", FieldKind.Url),
                new("draft", FieldKind.Flag)
            ],
            ["updates"] =
            [
                new("title", FieldKind.Text, Required: true, Max: 200),
                new("date", FieldKind.Date, Required: true),
                new("link", FieldKind.Link, Max: 500)
            ]
        };

    public static bool IsCollection(string name) => Collections.ContainsKey(name);
    public static bool IsValidSlug(string slug) => SlugPattern.IsMatch(slug);

    /// <summary>The updates collection only loads .md files; the others also accept .mdx.</summary>
    public static IReadOnlyList<string> ExtensionsFor(string collection) =>
        collection == "updates" ? [".md"] : [".md", ".mdx"];

    // ---- Parsing ------------------------------------------------------------------------

    /// <summary>Splits a Markdown file into its front matter and body.</summary>
    public static (Dictionary<string, object?> Frontmatter, string Body) Parse(string markdown)
    {
        var text = markdown.Replace("\r\n", "\n").TrimStart('﻿');
        if (!text.StartsWith("---\n")) throw new DomainException("This file has no front matter (the --- block at the top).");

        var close = text.IndexOf("\n---", 4, StringComparison.Ordinal);
        // An empty block ("---\n---") closes immediately.
        if (close < 0 && text.StartsWith("---\n---")) close = 3;
        if (close < 0) throw new DomainException("This file's front matter is never closed with ---.");

        var yaml = close <= 3 ? string.Empty : text[4..close];
        var afterFence = close + 4;
        var body = afterFence < text.Length ? text[afterFence..] : string.Empty;
        body = body.StartsWith("\n") ? body[1..] : body;
        if (body.StartsWith("\n")) body = body[1..];

        return (ParseYaml(yaml), body);
    }

    private static Dictionary<string, object?> ParseYaml(string yaml)
    {
        var result = new Dictionary<string, object?>(StringComparer.Ordinal);
        if (string.IsNullOrWhiteSpace(yaml)) return result;

        try
        {
            var stream = new YamlStream();
            stream.Load(new StringReader(yaml));
            if (stream.Documents.Count == 0 || stream.Documents[0].RootNode is not YamlMappingNode map)
                throw new DomainException("The front matter must be a list of key: value lines.");

            foreach (var (key, value) in map.Children)
            {
                if (key is not YamlScalarNode { Value: { } name }) throw new DomainException("A front-matter key is not plain text.");
                result[name] = ToObject(value, depth: 0);
            }

            return result;
        }
        catch (YamlException ex)
        {
            throw new DomainException($"The front matter is not valid YAML: {ex.Message}");
        }
    }

    private static object? ToObject(YamlNode node, int depth)
    {
        if (depth > 4) throw new DomainException("The front matter is nested too deeply.");

        switch (node)
        {
            case YamlScalarNode scalar:
                return ScalarValue(scalar);
            case YamlSequenceNode sequence:
                return sequence.Children.Select(c => ToObject(c, depth + 1)).ToList();
            case YamlMappingNode mapping:
                var dict = new Dictionary<string, object?>(StringComparer.Ordinal);
                foreach (var (k, v) in mapping.Children)
                {
                    if (k is not YamlScalarNode { Value: { } name }) throw new DomainException("A front-matter key is not plain text.");
                    dict[name] = ToObject(v, depth + 1);
                }
                return dict;
            default:
                throw new DomainException("Unsupported YAML in the front matter.");
        }
    }

    /// <summary>Quoted scalars are always text; plain ones become booleans or numbers the way YAML says they should.</summary>
    private static object? ScalarValue(YamlScalarNode scalar)
    {
        var value = scalar.Value ?? string.Empty;
        if (scalar.Style != ScalarStyle.Plain) return value;

        if (value is "" or "~" || value.Equals("null", StringComparison.OrdinalIgnoreCase)) return null;
        if (value.Equals("true", StringComparison.OrdinalIgnoreCase)) return true;
        if (value.Equals("false", StringComparison.OrdinalIgnoreCase)) return false;
        if (long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var whole)) return whole;
        if (double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var number) && !DateLike.IsMatch(value)) return number;

        // Dates deliberately stay text so they round-trip exactly as written.
        return value;
    }

    // ---- Validation and writing ---------------------------------------------------------

    /// <summary>Checks the entry against the collection's schema and returns the complete Markdown file.</summary>
    public static string Compose(string collection, IDictionary<string, object?> frontmatter, string body)
    {
        if (!Collections.TryGetValue(collection, out var specs)) throw new NotFoundException("Collection", collection);

        var errors = new List<string>();
        var values = frontmatter.ToDictionary(kv => kv.Key, kv => Normalize(kv.Value), StringComparer.Ordinal);

        if (body is null) errors.Add("body is required");
        else if (body.Length > MaxBodyLength) errors.Add($"body is longer than {MaxBodyLength:N0} characters");

        var lines = new List<string>();

        foreach (var spec in specs)
        {
            values.TryGetValue(spec.Key, out var raw);
            var line = ValidateField(spec, raw, errors);
            if (line is not null) lines.Add(line);
        }

        var known = specs.Select(s => s.Key).ToHashSet(StringComparer.Ordinal);
        foreach (var (key, raw) in values.Where(kv => !known.Contains(kv.Key)))
        {
            if (!UnknownKeyPattern.IsMatch(key)) { errors.Add($"'{key}' is not a valid field name"); continue; }
            try { lines.AddRange(EmitUnknown(key, raw, 0)); }
            catch (DomainException ex) { errors.Add($"{key}: {ex.Message}"); }
        }

        if (errors.Count > 0) throw new DomainException("Fix these first: " + string.Join("; ", errors) + ".");

        var normalizedBody = (body ?? string.Empty).Replace("\r\n", "\n").TrimEnd('\n');
        var builder = new StringBuilder("---\n");
        foreach (var line in lines) builder.Append(line).Append('\n');
        builder.Append("---\n");
        if (normalizedBody.Length > 0) builder.Append('\n').Append(normalizedBody).Append('\n');
        return builder.ToString();
    }

    private static string? ValidateField(FieldSpec spec, object? raw, List<string> errors)
    {
        var empty = raw is null || raw is string { Length: 0 } || raw is string s0 && string.IsNullOrWhiteSpace(s0);

        switch (spec.Kind)
        {
            case FieldKind.Text:
            case FieldKind.Link:
            case FieldKind.Url:
            {
                if (empty)
                {
                    if (spec.Required) errors.Add($"{spec.Key} is required");
                    return null;
                }
                if (raw is not string text) { errors.Add($"{spec.Key} must be text"); return null; }

                text = text.Trim();
                if (text.Length > spec.Max) { errors.Add($"{spec.Key} is longer than {spec.Max} characters"); return null; }
                if (text.Any(char.IsControl)) { errors.Add($"{spec.Key} must be a single line"); return null; }

                if (spec.Kind == FieldKind.Url && !IsHttpUrl(text))
                    { errors.Add($"{spec.Key} must be a link starting with http:// or https://"); return null; }
                if (spec.Kind == FieldKind.Link && !(IsHttpUrl(text) || (text.StartsWith('/') && !text.StartsWith("//"))))
                    { errors.Add($"{spec.Key} must be an http(s) link or a path starting with /"); return null; }

                return $"{spec.Key}: {Scalar(text, flow: false)}";
            }

            case FieldKind.Date:
            {
                if (empty) { errors.Add($"{spec.Key} is required"); return null; }
                if (raw is not string date || !DateTime.TryParseExact(date.Trim(), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed)
                    || parsed.Year is < 1990 or > 2100)
                { errors.Add($"{spec.Key} must be a real date written YYYY-MM-DD"); return null; }

                return $"{spec.Key}: {parsed:yyyy-MM-dd}";
            }

            case FieldKind.Flag:
            {
                if (raw is null) return null;
                if (raw is not bool flag) { errors.Add($"{spec.Key} must be true or false"); return null; }
                // The site defaults these to false, so only the true case needs writing.
                return flag ? $"{spec.Key}: true" : null;
            }

            case FieldKind.TextList:
            {
                if (raw is null) return null;
                if (raw is not List<object?> items) { errors.Add($"{spec.Key} must be a list"); return null; }

                var cleaned = new List<string>();
                foreach (var item in items)
                {
                    if (item is not string text || string.IsNullOrWhiteSpace(text)) { errors.Add($"{spec.Key} can only contain non-empty text"); return null; }
                    text = text.Trim();
                    if (text.Length > 40) { errors.Add($"each item in {spec.Key} must be 40 characters or fewer"); return null; }
                    if (text.Any(char.IsControl)) { errors.Add($"{spec.Key} items must be a single line"); return null; }
                    if (!cleaned.Contains(text, StringComparer.OrdinalIgnoreCase)) cleaned.Add(text);
                }

                if (cleaned.Count > spec.Max) { errors.Add($"{spec.Key} can have at most {spec.Max} items"); return null; }
                return cleaned.Count == 0 ? null : $"{spec.Key}: [{string.Join(", ", cleaned.Select(c => Scalar(c, flow: true)))}]";
            }

            case FieldKind.Impact:
            {
                if (raw is null) return null;
                if (raw is not Dictionary<string, object?> impact) { errors.Add("impact must have a value and a label"); return null; }

                var value = (impact.GetValueOrDefault("value") as string)?.Trim();
                var label = (impact.GetValueOrDefault("label") as string)?.Trim();

                // Both or neither: the site's schema requires the pair together.
                if (string.IsNullOrEmpty(value) && string.IsNullOrEmpty(label)) return null;
                if (string.IsNullOrEmpty(value) || string.IsNullOrEmpty(label)) { errors.Add("impact needs both a value and a label"); return null; }
                if (value.Length > 40 || label.Length > 80) { errors.Add("impact value must be 40 characters or fewer and its label 80 or fewer"); return null; }
                if (value.Any(char.IsControl) || label.Any(char.IsControl)) { errors.Add("impact must be single-line text"); return null; }

                return $"impact:\n  value: {Scalar(value, flow: false)}\n  label: {Scalar(label, flow: false)}";
            }

            default:
                return null;
        }
    }

    private static bool IsHttpUrl(string value) =>
        Uri.TryCreate(value, UriKind.Absolute, out var uri) && uri.Scheme is "http" or "https" && !string.IsNullOrEmpty(uri.Host);

    private static IEnumerable<string> EmitUnknown(string key, object? value, int depth)
    {
        if (depth > 3) throw new DomainException("is nested too deeply");
        var pad = new string(' ', depth * 2);

        switch (value)
        {
            case null: yield return $"{pad}{key}: null"; break;
            case bool flag: yield return $"{pad}{key}: {(flag ? "true" : "false")}"; break;
            case long or int: yield return $"{pad}{key}: {Convert.ToString(value, CultureInfo.InvariantCulture)}"; break;
            case double number: yield return $"{pad}{key}: {number.ToString("R", CultureInfo.InvariantCulture)}"; break;
            case string text:
                if (text.Length > 1000 || text.Any(char.IsControl)) throw new DomainException("must be single-line text under 1,000 characters");
                yield return $"{pad}{key}: {Scalar(text, flow: false, dateLooksLikeText: false)}";
                break;
            case List<object?> list:
                if (list.Count > 50 || list.Any(i => i is not string)) throw new DomainException("can only be a short list of text");
                yield return $"{pad}{key}: [{string.Join(", ", list.Cast<string>().Select(i => Scalar(i, flow: true)))}]";
                break;
            case Dictionary<string, object?> map:
                yield return $"{pad}{key}:";
                foreach (var (k, v) in map)
                {
                    if (!UnknownKeyPattern.IsMatch(k)) throw new DomainException($"'{k}' is not a valid field name");
                    foreach (var line in EmitUnknown(k, v, depth + 1)) yield return line;
                }
                break;
            default:
                throw new DomainException("has an unsupported value");
        }
    }

    /// <summary>
    /// Writes text the way the site's existing files do: bare when that reads back as the same text,
    /// double-quoted when YAML would otherwise treat it as something else (a number, a boolean, a comment...).
    /// </summary>
    internal static string Scalar(string value, bool flow, bool dateLooksLikeText = true)
    {
        if (NeedsQuotes(value, flow, dateLooksLikeText)) return Quote(value);
        return value;
    }

    private static bool NeedsQuotes(string value, bool flow, bool dateLooksLikeText)
    {
        if (value.Length == 0 || value != value.Trim()) return true;
        if (Reserved.Contains(value) || NumberLike.IsMatch(value)) return true;
        if (dateLooksLikeText && DateLike.IsMatch(value)) return true;
        if ("-?:,[]{}#&*!|>'\"%@`".Contains(value[0])) return true;
        if (value.Contains(": ") || value.Contains(" #") || value.EndsWith(':')) return true;
        if (flow && value.IndexOfAny([',', '[', ']', '{', '}']) >= 0) return true;
        return false;
    }

    private static string Quote(string value)
    {
        var builder = new StringBuilder("\"");
        foreach (var ch in value)
        {
            switch (ch)
            {
                case '\\': builder.Append("\\\\"); break;
                case '"': builder.Append("\\\""); break;
                case '\n': builder.Append("\\n"); break;
                case '\r': builder.Append("\\r"); break;
                case '\t': builder.Append("\\t"); break;
                default:
                    if (char.IsControl(ch)) builder.Append("\\u").Append(((int)ch).ToString("x4"));
                    else builder.Append(ch);
                    break;
            }
        }
        return builder.Append('"').ToString();
    }

    /// <summary>JSON arrives as <see cref="JsonElement"/>s; this turns them into the plain values the rules above expect.</summary>
    public static object? Normalize(object? value) => value switch
    {
        JsonElement { ValueKind: JsonValueKind.Null or JsonValueKind.Undefined } => null,
        JsonElement { ValueKind: JsonValueKind.String } e => e.GetString(),
        JsonElement { ValueKind: JsonValueKind.True } => true,
        JsonElement { ValueKind: JsonValueKind.False } => false,
        JsonElement { ValueKind: JsonValueKind.Number } e => e.TryGetInt64(out var whole) ? whole : e.GetDouble(),
        JsonElement { ValueKind: JsonValueKind.Array } e => e.EnumerateArray().Select(i => Normalize(i)).ToList(),
        JsonElement { ValueKind: JsonValueKind.Object } e => e.EnumerateObject().ToDictionary(p => p.Name, p => Normalize(p.Value), StringComparer.Ordinal),
        IDictionary<string, object?> d => d.ToDictionary(kv => kv.Key, kv => Normalize(kv.Value), StringComparer.Ordinal),
        IEnumerable<object?> list when value is not string => list.Select(Normalize).ToList(),
        _ => value
    };
}
