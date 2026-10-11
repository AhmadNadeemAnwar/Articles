using Articles.Application.Admin;
using Articles.Domain.Exceptions;

namespace Articles.Application.Images;

/// <summary>Makes a picture from a text description. Implementations throw a <see cref="DomainException"/> with a plain message on failure.</summary>
public interface IImageGenerator
{
    /// <summary>False until the service is set up, so the editor can hide the feature instead of failing.</summary>
    bool IsEnabled { get; }

    /// <summary>The picture's bytes, in a format <see cref="ImageFormat"/> recognises.</summary>
    Task<byte[]> GenerateAsync(string prompt, CancellationToken ct = default);
}

public sealed record GenerationStatus(bool Enabled, int UsedToday, int DailyLimit);

public sealed record GeneratedImage(byte[] Data, string ContentType, int UsedToday, int DailyLimit);

public interface IImageGenerationService
{
    Task<GenerationStatus> GetStatusAsync(CancellationToken ct = default);
    Task<GeneratedImage> GenerateAsync(string admin, string? prompt, CancellationToken ct = default);
}

/// <summary>
/// Generation runs on a free allowance, so it is capped here as well as by the provider: a hard
/// number of pictures a day, counted from the history in the database so a server restart cannot
/// reset it. Pictures are only previews until the writer keeps one, which then goes through the
/// ordinary upload.
/// </summary>
public sealed class ImageGenerationService : IImageGenerationService
{
    /// <summary>Pictures per UTC day. The provider's own free allowance also resets at midnight UTC.</summary>
    public const int DailyLimit = 20;

    public const int MinPromptLength = 3;
    public const int MaxPromptLength = 500;

    /// <summary>One at a time, so two quick presses cannot both pass the check below.</summary>
    private static readonly SemaphoreSlim Gate = new(1, 1);

    private readonly IImageGenerator _generator;
    private readonly IActivityLog _activity;
    private readonly TimeProvider _time;

    public ImageGenerationService(IImageGenerator generator, IActivityLog activity, TimeProvider? time = null)
    {
        _generator = generator;
        _activity = activity;
        _time = time ?? TimeProvider.System;
    }

    private DateTimeOffset StartOfToday()
    {
        var now = _time.GetUtcNow();
        return new DateTimeOffset(now.Year, now.Month, now.Day, 0, 0, 0, TimeSpan.Zero);
    }

    public async Task<GenerationStatus> GetStatusAsync(CancellationToken ct = default) =>
        new(_generator.IsEnabled, _generator.IsEnabled ? await _activity.CountSinceAsync(Activity.GeneratedImage, StartOfToday(), ct) : 0, DailyLimit);

    public async Task<GeneratedImage> GenerateAsync(string admin, string? prompt, CancellationToken ct = default)
    {
        if (!_generator.IsEnabled)
            throw new DomainException("Picture generation is not set up yet. The steps are in ADMIN.md, under Picture generation.");

        var text = (prompt ?? string.Empty).Trim();
        if (text.Length < MinPromptLength) throw new DomainException("Describe the picture you want, in a few words.");
        if (text.Length > MaxPromptLength) throw new DomainException($"Keep the description under {MaxPromptLength} characters.");

        await Gate.WaitAsync(ct);
        try
        {
            var used = await _activity.CountSinceAsync(Activity.GeneratedImage, StartOfToday(), ct);
            if (used >= DailyLimit)
                throw new DomainException($"You have made all {DailyLimit} pictures allowed today. This keeps the service free. The count resets at midnight UTC.");

            var data = await _generator.GenerateAsync(text, ct);
            var contentType = ImageFormat.Detect(data)
                ?? throw new DomainException("The service sent back something that is not a picture. Try again.");

            // Counted once the provider has spent its allowance on it, whether or not the writer keeps it.
            await _activity.RecordAsync(admin, Activity.GeneratedImage, text, ct);
            return new GeneratedImage(data, contentType, used + 1, DailyLimit);
        }
        finally
        {
            Gate.Release();
        }
    }
}
