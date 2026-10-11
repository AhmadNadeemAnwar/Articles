using Articles.Application.Common;
using Articles.Api.Middleware;
using Articles.Application.Images;
using Articles.Domain.Exceptions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Articles.Api.Controllers;

[ApiController]
[Route("api/v1/images")]
public class ImagesController : ControllerBase
{
    private readonly IImageService _images;
    private readonly ICurrentUser _currentUser;

    public ImagesController(IImageService images, ICurrentUser currentUser)
    {
        _images = images;
        _currentUser = currentUser;
    }

    /// <summary>Stores one picture for use in a post. Signed-in writers only.</summary>
    [HttpPost]
    [Authorize]
    // A little above the 2 MB image limit, to leave room for the form's own wrapping.
    [RequestSizeLimit(ImageService.MaxBytes + 64 * 1024)]
    [ProducesResponseType(typeof(ImageDto), StatusCodes.Status201Created)]
    public async Task<ActionResult<ImageDto>> Upload(IFormFile? file, CancellationToken ct)
    {
        if (file is null || file.Length == 0) throw new DomainException("Choose an image to upload.");
        if (file.Length > ImageService.MaxBytes) throw new DomainException("That image is too large. The limit is 2 MB.");

        using var buffer = new MemoryStream((int)file.Length);
        await file.CopyToAsync(buffer, ct);

        var image = await _images.UploadAsync(_currentUser.RequireUserId(), buffer.ToArray(), ct);
        return CreatedAtAction(nameof(Get), new { id = image.Id }, image);
    }

    /// <summary>The picture itself. Public, because published posts show it to every reader.</summary>
    [HttpGet("{id:guid}")]
    [AllowAnonymous]
    public async Task<IActionResult> Get(Guid id, CancellationToken ct)
    {
        var image = await _images.GetAsync(id, ct);

        // An image never changes once stored (a new upload gets a new id), so browsers may keep it for good.
        HttpContext.Items[SecurityHeadersMiddleware.PublicImageKey] = true;
        Response.Headers.CacheControl = "public, max-age=31536000, immutable";

        return File(image.Data, image.ContentType);
    }
}
