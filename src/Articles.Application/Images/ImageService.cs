using Articles.Domain.Common;
using Articles.Domain.Entities;
using Articles.Domain.Exceptions;
using Articles.Domain.Interfaces;

namespace Articles.Application.Images;

public sealed record ImageDto(Guid Id, string Path, string ContentType, int Size);

public sealed record ImageContent(byte[] Data, string ContentType);

public interface IImageService
{
    Task<ImageDto> UploadAsync(Guid ownerId, byte[] data, CancellationToken ct = default);
    Task<ImageContent> GetAsync(Guid id, CancellationToken ct = default);
}

public sealed class ImageService : IImageService
{
    /// <summary>The editor shrinks pictures before sending them, so a real upload is far below this.</summary>
    public const int MaxBytes = 2 * 1024 * 1024;

    /// <summary>
    /// Ceiling for all images together. The free database holds 0.5 GB in total, and posts, accounts
    /// and indexes need room too, so images stop well short of it.
    /// </summary>
    public const long MaxTotalBytes = 300L * 1024 * 1024;

    private readonly IImageRepository _images;
    private readonly IUnitOfWork _unitOfWork;

    public ImageService(IImageRepository images, IUnitOfWork unitOfWork)
    {
        _images = images;
        _unitOfWork = unitOfWork;
    }

    public async Task<ImageDto> UploadAsync(Guid ownerId, byte[] data, CancellationToken ct = default)
    {
        if (data is null || data.Length == 0) throw new DomainException("Choose an image to upload.");
        if (data.Length > MaxBytes) throw new DomainException("That image is too large. The limit is 2 MB.");

        // The type comes from the file's own first bytes, never from what the browser claims it is.
        var contentType = ImageFormat.Detect(data)
            ?? throw new DomainException("Only JPEG, PNG and WebP images can be uploaded.");

        if (await _images.GetTotalBytesAsync(ct) + data.Length > MaxTotalBytes)
            throw new DomainException("Image storage is full. Use smaller images, or remove ones you no longer need.");

        var image = new StoredImage(ownerId, contentType, data);
        await _images.AddAsync(image, ct);
        await _unitOfWork.SaveChangesAsync(ct);

        return new ImageDto(image.Id, UrlRules.StoredImagePath(image.Id), image.ContentType, image.Size);
    }

    public async Task<ImageContent> GetAsync(Guid id, CancellationToken ct = default)
    {
        var image = await _images.GetAsync(id, ct) ?? throw new NotFoundException(nameof(StoredImage), id);
        return new ImageContent(image.Data, image.ContentType);
    }
}

/// <summary>
/// Recognises the three picture formats the site accepts by their signature bytes. SVG is left out
/// on purpose: it can carry script. GIF is left out because animations are large.
/// </summary>
public static class ImageFormat
{
    public static string? Detect(ReadOnlySpan<byte> data)
    {
        if (data.Length >= 3 && data[0] == 0xFF && data[1] == 0xD8 && data[2] == 0xFF) return "image/jpeg";

        ReadOnlySpan<byte> png = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];
        if (data.Length >= png.Length && data[..png.Length].SequenceEqual(png)) return "image/png";

        if (data.Length >= 12 && data[..4].SequenceEqual("RIFF"u8) && data.Slice(8, 4).SequenceEqual("WEBP"u8)) return "image/webp";

        return null;
    }
}
