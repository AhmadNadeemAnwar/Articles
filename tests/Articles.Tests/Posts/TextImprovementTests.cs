using FluentAssertions;
using Articles.Application.Admin;
using Articles.Application.Posts;
using Articles.Domain.Exceptions;
using Articles.Infrastructure.Persistence;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Articles.Tests.Posts;

/// <summary>"Improve text with AI": the structure is never the AI's to change, and links keep their addresses.</summary>
public class TextImprovementTests : IDisposable
{
    private sealed class FakeModel : ITextModel
    {
        public bool IsEnabled { get; set; } = true;
        public Queue<Func<string, string>> Replies { get; } = new();
        public List<(string Instruction, string Request, int MaxTokens, double Temperature)> Calls { get; } = [];

        public Task<string> CompleteAsync(string instruction, string request, int maxTokens, double temperature, CancellationToken ct = default)
        {
            Calls.Add((instruction, request, maxTokens, temperature));
            var reply = Replies.Count > 0 ? Replies.Dequeue() : Echo;
            return Task.FromResult(reply(request));
        }

        public static string Echo(string request) => request;
    }

    private readonly TestDatabase _fixture = new();
    private readonly ManualTimeProvider _clock = new(new DateTimeOffset(2026, 10, 14, 10, 0, 0, TimeSpan.Zero));
    private readonly FakeModel _model = new();
    private readonly ActivityLog _log;
    private readonly TextImprovementService _service;

    public TextImprovementTests()
    {
        _log = new ActivityLog(_fixture.Db, NullLogger<ActivityLog>.Instance, _clock);
        _service = new TextImprovementService(_model, _log, _clock);
    }

    public void Dispose() => _fixture.Dispose();

    private static ImproveTextRequest Lines(params string[] lines) => new(lines);

    // ---- Improving ------------------------------------------------------------------------------

    [Fact]
    public async Task The_lines_come_back_improved_in_the_same_order_and_number()
    {
        _model.Replies.Enqueue(_ => "[1] The first line, improved.\n[2] The second line, improved.");

        var result = await _service.ImproveAsync("owner@example.com", Lines("the first line", "the second line"));

        result.Units.Should().Equal("The first line, improved.", "The second line, improved.");
        result.Changed.Should().Be(2);
        result.UsedToday.Should().Be(1);
        result.DailyLimit.Should().Be(20);
    }

    [Fact]
    public async Task The_lines_are_sent_numbered_with_the_editing_brief_and_a_low_temperature()
    {
        await _service.ImproveAsync("owner@example.com", Lines("one", "two"));

        var call = _model.Calls.Single();
        call.Request.Should().Be("[1] one\n[2] two");
        call.Instruction.Should().Contain("Never add new claims").And.Contain("Do not merge, split").And.Contain("Urdu stays Urdu").And.Contain("**bold**");
        call.Temperature.Should().BeLessThan(0.7);
        call.MaxTokens.Should().BeInRange(300, 3500);
    }

    [Fact]
    public async Task Line_breaks_inside_a_line_travel_as_a_symbol_and_come_back_as_line_breaks()
    {
        _model.Replies.Enqueue(request => request.Replace("[1] ", "[1] Better: "));

        var result = await _service.ImproveAsync("owner@example.com", Lines("first\nsecond"));

        _model.Calls.Single().Request.Should().Be("[1] first⏎second");
        result.Units.Single().Should().Be("Better: first\nsecond");
    }

    [Fact]
    public async Task Whatever_else_the_model_says_around_the_lines_is_ignored()
    {
        _model.Replies.Enqueue(_ => "Sure, here you go:\n\n[1] Improved one.\n[2] Improved two.\n\nHope that helps!");

        (await _service.ImproveAsync("owner@example.com", Lines("one", "two"))).Units.Should().Equal("Improved one.", "Improved two.");
    }

    [Fact]
    public async Task Bold_italic_and_link_markup_in_a_line_is_kept_when_the_model_keeps_it()
    {
        _model.Replies.Enqueue(_ => "[1] Read **this** _carefully_ on [my page](https://example.com/a).");

        var result = await _service.ImproveAsync("owner@example.com", Lines("read **this** _careful_ on [my page](https://example.com/a)"));

        result.Units.Single().Should().Be("Read **this** _carefully_ on [my page](https://example.com/a).");
    }

    // ---- Keeping the structure ------------------------------------------------------------------

    [Fact]
    public async Task A_line_whose_link_address_was_changed_or_dropped_stays_as_the_writer_wrote_it()
    {
        _model.Replies.Enqueue(_ => "[1] See [here](https://evil.example).\n[2] Plain, improved.\n[3] No link now.");
        _model.Replies.Enqueue(_ => "[1] See [here](https://evil.example).\n[2] Plain, improved.\n[3] No link now.");

        var result = await _service.ImproveAsync("owner@example.com",
            Lines("see [here](https://example.com/a).", "plain text here", "a line with [a link](https://example.com/b)"));

        result.Units.Should().Equal("see [here](https://example.com/a).", "Plain, improved.", "a line with [a link](https://example.com/b)");
        result.Changed.Should().Be(1);
    }

    [Fact]
    public async Task A_line_the_model_leaves_out_stays_as_it_was_and_the_rest_are_used()
    {
        _model.Replies.Enqueue(_ => "[1] First, improved.\n[3] Third, improved.");
        _model.Replies.Enqueue(_ => "[1] First, improved.\n[3] Third, improved.");

        var result = await _service.ImproveAsync("owner@example.com", Lines("first", "second", "third"));

        result.Units.Should().Equal("First, improved.", "second", "Third, improved.");
    }

    [Fact]
    public async Task A_second_try_is_made_when_the_first_answer_is_not_complete()
    {
        _model.Replies.Enqueue(_ => "[1] Only the first.");
        _model.Replies.Enqueue(_ => "[1] First, improved.\n[2] Second, improved.");

        var result = await _service.ImproveAsync("owner@example.com", Lines("first", "second"));

        result.Units.Should().Equal("First, improved.", "Second, improved.");
        _model.Calls.Should().HaveCount(2);
        result.UsedToday.Should().Be(1, "a retry is not a second go");
    }

    [Theory]
    [InlineData("an ordinary paragraph that is long enough to be measured properly", "")]
    [InlineData("an ordinary paragraph that is long enough to be measured properly", "ok")]
    [InlineData("an ordinary paragraph that is long enough to be measured properly", "an ordinary paragraph that is long enough to be measured properly, and then a great deal more that the model decided to add on its own account to the end of it")]
    public void A_line_that_became_empty_or_much_shorter_or_much_longer_is_not_accepted(string original, string improved)
    {
        TextImprovementService.Acceptable(original, improved).Should().BeFalse();
    }

    [Fact]
    public void An_improvement_of_a_similar_length_with_the_same_links_is_accepted()
    {
        TextImprovementService.Acceptable("it was good [x](https://a.example)", "It was good indeed [x](https://a.example)").Should().BeTrue();
        TextImprovementService.Acceptable("", "").Should().BeTrue();
        TextImprovementService.Acceptable("   ", "").Should().BeTrue();
        TextImprovementService.Acceptable("", "something invented").Should().BeFalse();
    }

    [Fact]
    public async Task When_nothing_usable_comes_back_after_both_tries_it_says_so_and_the_text_is_untouched()
    {
        _model.Replies.Enqueue(_ => "I cannot do that.");
        _model.Replies.Enqueue(_ => "Still no.");

        var failure = await Record.ExceptionAsync(() => _service.ImproveAsync("owner@example.com", Lines("first", "second")));

        failure.Should().BeOfType<DomainException>().Which.Message.Should().Contain("could be used");
    }

    // ---- Limits ---------------------------------------------------------------------------------

    [Fact]
    public void A_long_section_is_cut_into_pieces_at_line_boundaries_that_each_fit_one_go()
    {
        var lines = Enumerable.Range(0, 12).Select(_ => new string('x', 1500)).ToList();

        var chunks = TextImprovementService.Chunk(lines);

        chunks.SelectMany(c => c).Should().Equal(Enumerable.Range(0, 12));
        chunks.Should().OnlyContain(c => c.Sum(i => lines[i].Length) <= TextImprovementService.ChunkCharacters);
        chunks.Count.Should().BeGreaterThan(2);
    }

    [Fact]
    public async Task Too_much_text_for_one_go_is_refused_so_the_editor_sends_it_in_pieces()
    {
        var failure = await Record.ExceptionAsync(() => _service.ImproveAsync("owner@example.com", Lines(new string('x', 3000), new string('y', 3000))));

        failure.Should().BeOfType<DomainException>().Which.Message.Should().Contain("at a time");
        _model.Calls.Should().BeEmpty();
    }

    [Theory]
    [InlineData(301)]
    public async Task More_lines_than_a_section_can_have_are_refused(int count)
    {
        var failure = await Record.ExceptionAsync(() => _service.ImproveAsync("owner@example.com", new ImproveTextRequest(Enumerable.Repeat("x", count).ToList())));

        failure.Should().BeOfType<DomainException>();
        _model.Calls.Should().BeEmpty();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task Nothing_to_improve_is_a_plain_message_and_asks_nobody(string? text)
    {
        var failure = await Record.ExceptionAsync(() => _service.ImproveAsync("owner@example.com", new ImproveTextRequest(text is null ? null : [text])));

        failure.Should().BeOfType<DomainException>().Which.Message.Should().Contain("no text");
        _model.Calls.Should().BeEmpty();
    }

    [Fact]
    public async Task Each_go_is_recorded_and_the_daily_limit_stops_more_until_tomorrow()
    {
        for (var i = 0; i < TextImprovementService.DailyLimit; i++) await _service.ImproveAsync("owner@example.com", Lines("some text"));

        var failure = await Record.ExceptionAsync(() => _service.ImproveAsync("owner@example.com", Lines("some text")));
        failure.Should().BeOfType<DomainException>().Which.Message.Should().Contain("20 AI goes");
        (await _log.GetPageAsync(1, 50)).Items.Count(e => e.Action == Activity.ImprovedText).Should().Be(20);
        _model.Calls.Should().HaveCount(20);

        _clock.Advance(TimeSpan.FromDays(1));
        (await _service.GetStatusAsync()).UsedToday.Should().Be(0);
        (await _service.ImproveAsync("owner@example.com", Lines("some text"))).UsedToday.Should().Be(1);
    }

    [Fact]
    public async Task Without_the_ai_set_up_it_says_so_and_asks_nobody()
    {
        _model.IsEnabled = false;

        var failure = await Record.ExceptionAsync(() => _service.ImproveAsync("owner@example.com", Lines("text")));

        failure.Should().BeOfType<DomainException>().Which.Message.Should().Contain("not set up");
        (await _service.GetStatusAsync()).Should().Be(new TextImprovementStatus(false, 0, 20));
    }
}
