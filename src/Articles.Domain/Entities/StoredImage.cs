using Articles.Domain.Common;
using Articles.Domain.Exceptions;

namespace Articles.Domain.Entities;

/// <summary>
/// An uploaded picture, kept in the database so the site needs no separate storage service.
/// Never loaded as part of a post: it is fetched by id, on its own, when a browser asks for it.
/// </summary>
public class StoredImage : BaseEntity
{
    public Guid OwnerId { get; private set; }
    public string ContentType { get; private set; } = null!;
    public byte[] Data { get; private set; } = null!;
    public int Size { get; private set; }

    private StoredImage() { }

    public StoredImage(Guid ownerId, string contentType, byte[] data)
    {
        if (data is null || data.Length == 0) throw new DomainException("An image cannot be empty.");
        if (string.IsNullOrWhiteSpace(contentType)) throw new DomainException("An image needs a type.");

        OwnerId = ownerId;
        ContentType = contentType;
        Data = data;
        Size = data.Length;
    }
}
