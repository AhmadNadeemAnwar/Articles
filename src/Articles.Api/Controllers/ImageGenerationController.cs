using System.Security.Claims;
using Articles.Api.Common;
using Articles.Application.Images;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Articles.Api.Controllers;

public sealed record GenerateImageRequest(string? Prompt);

public sealed record GeneratedImageResponse(string ImageBase64, string ContentType, int UsedToday, int DailyLimit);

/// <summary>Makes a picture from a description, for the post editor. Admin only; the picture is a preview until the writer keeps it.</summary>
[ApiController]
[Route("api/v1/admin/images")]
[Authorize(Policy = AdminRequirement.PolicyName)]
public class ImageGenerationController : ControllerBase
{
    private readonly IImageGenerationService _generation;

    public ImageGenerationController(IImageGenerationService generation) => _generation = generation;

    private string Admin => User.FindFirstValue(ClaimTypes.Email) ?? User.FindFirstValue("email") ?? "unknown";

    /// <summary>Whether generation is set up, and how many pictures are left today.</summary>
    [HttpGet("generation")]
    public async Task<ActionResult<GenerationStatus>> Status(CancellationToken ct) => Ok(await _generation.GetStatusAsync(ct));

    [HttpPost("generate")]
    public async Task<ActionResult<GeneratedImageResponse>> Generate(GenerateImageRequest request, CancellationToken ct)
    {
        var image = await _generation.GenerateAsync(Admin, request.Prompt, ct);
        return Ok(new GeneratedImageResponse(Convert.ToBase64String(image.Data), image.ContentType, image.UsedToday, image.DailyLimit));
    }
}
