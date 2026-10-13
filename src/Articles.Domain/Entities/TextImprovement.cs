using Articles.Domain.Common;
using Articles.Domain.Exceptions;

namespace Articles.Domain.Entities;

/// <summary>
/// A section of text as it was before the writer replaced it with the AI's improved version, kept for good
/// so the original can always be looked at again. Both texts are the editor's own JSON, so every bold,
/// italic, link and list survives exactly.
/// </summary>
public class TextImprovement : BaseEntity
{
    /// <summary>Each text is a few thousand words at most; this leaves plenty of room for the formatting around them.</summary>
    public const int MaxJsonLength = 200_000;

    public Guid PostId { get; private set; }
    public Post Post { get; private set; } = null!;
    public string OriginalJson { get; private set; } = null!;
    public string ImprovedJson { get; private set; } = null!;
    public string Admin { get; private set; } = null!;

    private TextImprovement() { }

    public TextImprovement(Guid postId, string originalJson, string improvedJson, string admin)
    {
        if (string.IsNullOrWhiteSpace(originalJson) || originalJson.Length > MaxJsonLength) throw new DomainException("The original text is missing or too long to keep.");
        if (string.IsNullOrWhiteSpace(improvedJson) || improvedJson.Length > MaxJsonLength) throw new DomainException("The improved text is missing or too long to keep.");

        PostId = postId;
        OriginalJson = originalJson;
        ImprovedJson = improvedJson;
        Admin = admin;
    }
}
