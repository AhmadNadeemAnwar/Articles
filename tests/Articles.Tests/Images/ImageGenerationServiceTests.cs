using FluentAssertions;
using Articles.Application.Admin;
using Articles.Application.Images;
using Articles.Domain.Exceptions;
using Articles.Infrastructure.Persistence;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Articles.Tests.Images;

public class ImageGenerationServiceTests : IDisposable
{
    private static readonly byte[] Jpeg = [0xFF, 0xD8, 0xFF, 0xE0, 0x00, 0x10];

    private sealed class FakeGenerator : IImageGenerator
    {
        public bool IsEnabled { get; set; } = true;
        public byte[] Result { get; set; } = Jpeg;
        public Exception? Fail { get; set; }
        public List<string> Prompts { get; } = [];

        public Task<byte[]> GenerateAsync(string prompt, CancellationToken ct = default)
        {
            Prompts.Add(prompt);
            if (Fail is not null) throw Fail;
            return Task.FromResult(Result);
        }
    }

    private readonly TestDatabase _fixture = new();
    private readonly ManualTimeProvider _clock = new(new DateTimeOffset(2026, 10, 7, 15, 30, 0, TimeSpan.Zero));
    private readonly FakeGenerator _generator = new();
    private readonly ActivityLog _log;
    private readonly ImageGenerationService _service;

    public ImageGenerationServiceTests()
    {
        _log = new ActivityLog(_fixture.Db, NullLogger<ActivityLog>.Instance, _clock);
        _service = new ImageGenerationService(_generator, _log, _clock);
    }

    public void Dispose() => _fixture.Dispose();

    [Fact]
    public async Task A_description_makes_a_picture_and_reports_how_many_are_used()
    {
        var image = await _service.GenerateAsync("owner@example.com", "a lighthouse at dusk");

        image.Data.Should().Equal(Jpeg);
        image.ContentType.Should().Be("image/jpeg");
        image.UsedToday.Should().Be(1);
        image.DailyLimit.Should().Be(20);
        _generator.Prompts.Should().Equal("a lighthouse at dusk");
    }

    [Fact]
    public async Task The_description_is_trimmed_before_it_is_sent()
    {
        await _service.GenerateAsync("owner@example.com", "   a red kite  ");

        _generator.Prompts.Should().Equal("a red kite");
    }

    [Fact]
    public async Task Each_picture_is_recorded_in_the_history_with_who_asked_and_what_for()
    {
        await _service.GenerateAsync("owner@example.com", "a lighthouse at dusk");

        var entry = (await _log.GetPageAsync(1, 10)).Items.Single();
        entry.Actor.Should().Be("owner@example.com");
        entry.Action.Should().Be(Activity.GeneratedImage);
        entry.Subject.Should().Be("a lighthouse at dusk");
    }

    [Fact]
    public async Task Exactly_twenty_pictures_are_allowed_in_a_day()
    {
        for (var i = 0; i < 20; i++) await _service.GenerateAsync("owner@example.com", $"picture {i}");

        var act = () => _service.GenerateAsync("owner@example.com", "one too many");

        (await act.Should().ThrowAsync<DomainException>()).WithMessage("*all 20*");
        _generator.Prompts.Should().HaveCount(20, "the provider is never called once the cap is reached");
    }

    [Fact]
    public async Task The_count_starts_again_at_midnight_UTC()
    {
        for (var i = 0; i < 20; i++) await _service.GenerateAsync("owner@example.com", $"picture {i}");

        _clock.Advance(TimeSpan.FromHours(9)); // 00:30 the next day

        var image = await _service.GenerateAsync("owner@example.com", "a new day");
        image.UsedToday.Should().Be(1);
    }

    [Fact]
    public async Task The_count_survives_a_server_restart_because_it_is_kept_in_the_database()
    {
        for (var i = 0; i < 20; i++) await _service.GenerateAsync("owner@example.com", $"picture {i}");

        var afterRestart = new ImageGenerationService(_generator, new ActivityLog(_fixture.Db, NullLogger<ActivityLog>.Instance, _clock), _clock);

        (await afterRestart.GetStatusAsync()).UsedToday.Should().Be(20);
        var act = () => afterRestart.GenerateAsync("owner@example.com", "after the restart");
        await act.Should().ThrowAsync<DomainException>();
    }

    [Fact]
    public async Task Other_history_lines_do_not_use_up_the_allowance()
    {
        for (var i = 0; i < 25; i++) await _log.RecordAsync("owner@example.com", Activity.SignedIn);

        (await _service.GetStatusAsync()).UsedToday.Should().Be(0);
    }

    [Fact]
    public async Task Many_presses_at_once_still_stop_at_twenty()
    {
        var results = await Task.WhenAll(Enumerable.Range(0, 30).Select(async i =>
        {
            try { await _service.GenerateAsync("owner@example.com", $"picture {i}"); return true; }
            catch (DomainException) { return false; }
        }));

        results.Count(r => r).Should().Be(20);
        _generator.Prompts.Should().HaveCount(20);
    }

    [Fact]
    public async Task Too_short_or_too_long_a_description_is_refused_without_calling_the_provider()
    {
        foreach (var bad in new[] { null, "", "  ", "ab", new string('x', ImageGenerationService.MaxPromptLength + 1) })
        {
            var act = () => _service.GenerateAsync("owner@example.com", bad);
            await act.Should().ThrowAsync<DomainException>();
        }

        _generator.Prompts.Should().BeEmpty();
        (await _service.GetStatusAsync()).UsedToday.Should().Be(0);
    }

    [Fact]
    public async Task Before_it_is_set_up_generation_says_so_and_calls_nothing()
    {
        _generator.IsEnabled = false;

        var act = () => _service.GenerateAsync("owner@example.com", "a lighthouse");

        (await act.Should().ThrowAsync<DomainException>()).WithMessage("*not set up*");
        _generator.Prompts.Should().BeEmpty();
    }

    [Fact]
    public async Task A_failed_attempt_does_not_use_up_the_allowance()
    {
        _generator.Fail = new DomainException("The picture service could not be reached.");

        var act = () => _service.GenerateAsync("owner@example.com", "a lighthouse");
        await act.Should().ThrowAsync<DomainException>();

        (await _service.GetStatusAsync()).UsedToday.Should().Be(0);
    }

    [Fact]
    public async Task A_reply_that_is_not_a_picture_is_refused_and_not_counted()
    {
        _generator.Result = "<html>not an image</html>"u8.ToArray();

        var act = () => _service.GenerateAsync("owner@example.com", "a lighthouse");

        (await act.Should().ThrowAsync<DomainException>()).WithMessage("*not a picture*");
        (await _service.GetStatusAsync()).UsedToday.Should().Be(0);
    }

    [Fact]
    public async Task Status_reports_whether_it_is_set_up_and_how_many_are_used()
    {
        await _service.GenerateAsync("owner@example.com", "one");
        await _service.GenerateAsync("owner@example.com", "two");

        var status = await _service.GetStatusAsync();

        status.Should().Be(new GenerationStatus(true, 2, 20));

        _generator.IsEnabled = false;
        (await _service.GetStatusAsync()).Should().Be(new GenerationStatus(false, 0, 20));
    }
}
