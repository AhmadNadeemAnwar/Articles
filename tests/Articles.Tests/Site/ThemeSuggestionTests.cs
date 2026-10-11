using System.Net;
using System.Text.Json;
using FluentAssertions;
using Articles.Application.Admin;
using Articles.Application.Site;
using Articles.Domain.Exceptions;
using Articles.Infrastructure.Images;
using Articles.Infrastructure.Persistence;
using Articles.Infrastructure.Persistence.Repositories;
using Articles.Tests.Security;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace Articles.Tests.Site;

/// <summary>"Amaze with AI": the AI's colours are checked like colours typed by hand, and the use is capped and recorded.</summary>
public class ThemeSuggestionTests : IDisposable
{
    private const string Good = "{\"name\":\"Dusk Orchard\",\"header\":\"#3b1f4a\",\"accent\":\"#e0457b\",\"background\":\"#fff8f2\"}";

    private sealed class FakeAi : IThemeAi
    {
        public bool IsEnabled { get; set; } = true;
        public Queue<string> Replies { get; } = new();
        public List<string> Requests { get; } = [];

        public Task<string> AskAsync(string instruction, string request, CancellationToken ct = default)
        {
            Requests.Add(request);
            return Task.FromResult(Replies.Count > 0 ? Replies.Dequeue() : Good);
        }
    }

    private readonly TestDatabase _fixture = new();
    private readonly ManualTimeProvider _clock = new(new DateTimeOffset(2026, 10, 11, 15, 0, 0, TimeSpan.Zero));
    private readonly FakeAi _ai = new();
    private readonly ActivityLog _log;
    private readonly ThemeSuggestionService _service;

    public ThemeSuggestionTests()
    {
        _log = new ActivityLog(_fixture.Db, NullLogger<ActivityLog>.Instance, _clock);
        _service = new ThemeSuggestionService(_ai, new SiteRepository(_fixture.Db), _log, _clock);
    }

    public void Dispose() => _fixture.Dispose();

    [Fact]
    public async Task The_ai_proposes_three_colours_and_a_name_and_nothing_is_saved()
    {
        var suggestion = await _service.SuggestAsync("owner@example.com");

        suggestion.Colors.Should().Be(new ThemeColorsDto("#3b1f4a", "#fff8f2", "#e0457b"));
        suggestion.Name.Should().Be("Dusk Orchard");
        suggestion.UsedToday.Should().Be(1);
        suggestion.DailyLimit.Should().Be(20);
        _fixture.Db.SiteSettings.Should().BeEmpty("the owner keeps the colours with the ordinary theme save");
    }

    [Fact]
    public async Task Words_around_the_answer_and_a_code_fence_are_ignored()
    {
        _ai.Replies.Enqueue("Sure! Here you go:\n```json\n" + Good + "\n```\nEnjoy.");

        (await _service.SuggestAsync("owner@example.com")).Colors.Accent.Should().Be("#e0457b");
    }

    [Theory]
    [InlineData("no colours at all")]
    [InlineData("{\"header\":\"red\",\"accent\":\"#e0457b\",\"background\":\"#ffffff\"}")]
    [InlineData("{\"header\":\"#3b1f4a\",\"accent\":\"#e0457b\",\"background\":\"#808080\"}")]
    [InlineData("{\"header\":\"#3b1f4a\",\"accent\":\"#e0457b\",\"background\":\"#fff;}body{display:none\"}")]
    [InlineData("{\"header\":\"#3b1f4a\"}")]
    [InlineData("[1,2,3]")]
    public async Task An_answer_that_would_not_pass_as_typed_colours_is_asked_for_again(string bad)
    {
        _ai.Replies.Enqueue(bad);

        var suggestion = await _service.SuggestAsync("owner@example.com");

        suggestion.Colors.Main.Should().Be("#3b1f4a");
        _ai.Requests.Should().HaveCount(2);
    }

    [Fact]
    public async Task After_three_bad_answers_it_says_so_and_still_counts_as_one_use()
    {
        for (var i = 0; i < 3; i++) _ai.Replies.Enqueue("nonsense");

        var failure = await Record.ExceptionAsync(() => _service.SuggestAsync("owner@example.com"));

        failure.Should().BeOfType<DomainException>().Which.Message.Should().Contain("did not come up with colours");
        (await _service.GetStatusAsync()).UsedToday.Should().Be(1);
    }

    [Fact]
    public async Task The_theme_name_keeps_to_plain_characters_and_a_short_length()
    {
        _ai.Replies.Enqueue("{\"name\":\"<script>alert(1)</script> " + new string('x', 80) + "\",\"header\":\"#3b1f4a\",\"accent\":\"#e0457b\",\"background\":\"#fff8f2\"}");

        var name = (await _service.SuggestAsync("owner@example.com")).Name;

        name.Should().NotContain("<").And.NotContain(">").And.NotContain("(");
        name.Length.Should().BeLessThanOrEqualTo(40);
    }

    [Fact]
    public async Task Each_use_is_recorded_and_the_daily_limit_stops_further_ones_until_tomorrow()
    {
        for (var i = 0; i < ThemeSuggestionService.DailyLimit; i++) await _service.SuggestAsync("owner@example.com");

        var failure = await Record.ExceptionAsync(() => _service.SuggestAsync("owner@example.com"));
        failure.Should().BeOfType<DomainException>().Which.Message.Should().Contain("20 times today");
        (await _log.GetPageAsync(1, 50)).Items.Count(e => e.Action == Activity.AskedAiForColours).Should().Be(20);

        _clock.Advance(TimeSpan.FromDays(1));
        (await _service.GetStatusAsync()).UsedToday.Should().Be(0);
        (await _service.SuggestAsync("owner@example.com")).UsedToday.Should().Be(1);
    }

    [Fact]
    public async Task It_asks_for_something_different_from_the_colours_in_use()
    {
        await new SiteService(new SiteRepository(_fixture.Db), _fixture.Db, _log)
            .UpdateSettingsAsync(new UpdateSiteSettingsRequest("custom", "#17694a", "#ffffff", "#b35a00"), "owner@example.com");

        await _service.SuggestAsync("owner@example.com");

        _ai.Requests.Single().Should().Contain("#17694a").And.Contain("#b35a00").And.Contain("#ffffff");
    }

    [Fact]
    public async Task The_ai_is_given_the_editorial_brief_and_a_direction_to_vary_each_time()
    {
        ThemeSuggestionService.Instruction.Should().Contain("premium digital publication").And.Contain("Never pure white, never pure black").And.Contain("ONLY one JSON object");

        for (var i = 0; i < 18; i++) await _service.SuggestAsync("owner@example.com");

        _ai.Requests.Should().OnlyContain(r => r.StartsWith("Design a fresh theme this time:"));
        _ai.Requests.Distinct().Count().Should().BeGreaterThan(4, "each go steers the AI somewhere different");
    }

    [Fact]
    public async Task Without_the_service_set_up_it_says_so_and_asks_nobody()
    {
        _ai.IsEnabled = false;

        var failure = await Record.ExceptionAsync(() => _service.SuggestAsync("owner@example.com"));

        failure.Should().BeOfType<DomainException>().Which.Message.Should().Contain("not set up");
        _ai.Requests.Should().BeEmpty();
        (await _service.GetStatusAsync()).Should().Be(new ThemeAiStatus(false, 0, 20));
    }
}

public class CloudflareThemeAiTests
{
    private const string ValidId = "9abb8165ab250861d49f8f0f289a0823";

    private static CloudflareThemeAi Ai(HttpMessageHandler handler, ImageGenerationOptions? options = null) =>
        new(new HttpClient(handler), Options.Create(options ?? new ImageGenerationOptions { AccountId = ValidId, ApiToken = "secret-token" }), NullLogger<CloudflareThemeAi>.Instance);

    private static StubHandler Replying(HttpStatusCode status, string body) =>
        new(_ => new HttpResponseMessage(status) { Content = new StringContent(body) });

    [Fact]
    public void It_is_switched_off_until_the_account_and_the_token_are_set()
    {
        Ai(Replying(HttpStatusCode.OK, "{}"), new ImageGenerationOptions()).IsEnabled.Should().BeFalse();
        Ai(Replying(HttpStatusCode.OK, "{}")).IsEnabled.Should().BeTrue();
    }

    [Fact]
    public async Task The_question_goes_to_the_text_model_with_the_token_and_the_answer_is_read()
    {
        var handler = Replying(HttpStatusCode.OK, "{\"result\":{\"response\":\"{\\\"header\\\":\\\"#000000\\\"}\"},\"success\":true}");

        var answer = await Ai(handler).AskAsync("be a designer", "pick colours");

        answer.Should().Be("{\"header\":\"#000000\"}");
        var (request, body) = handler.Calls.Single();
        request.RequestUri!.AbsoluteUri.Should().Be("https://api.cloudflare.com/client/v4/accounts/" + ValidId + "/ai/run/@cf/meta/llama-3.1-8b-instruct");
        request.Headers.Authorization!.ToString().Should().Be("Bearer secret-token");
        var messages = JsonDocument.Parse(body).RootElement.GetProperty("messages");
        messages[0].GetProperty("content").GetString().Should().Be("be a designer");
        messages[1].GetProperty("content").GetString().Should().Be("pick colours");
    }

    [Theory]
    [InlineData("{\"result\":{\"response\":{\"header\":\"#111111\"}},\"success\":true}", "{\"header\":\"#111111\"}")]
    [InlineData("{\"result\":{\"choices\":[{\"message\":{\"content\":\"{\\\"header\\\":\\\"#222222\\\"}\"}}]}}", "{\"header\":\"#222222\"}")]
    [InlineData("{\"result\":{\"choices\":[{\"message\":{\"content\":{\"header\":\"#333333\"}}}]}}", "{\"header\":\"#333333\"}")]
    [InlineData("{\"response\":\"plain\"}", "plain")]
    public async Task Every_shape_workers_ai_answers_in_is_read(string reply, string expected)
    {
        (await Ai(Replying(HttpStatusCode.OK, reply)).AskAsync("a", "b")).Should().Be(expected);
    }

    [Theory]
    [InlineData("{\"result\":{\"response\":\"\"}}")]
    [InlineData("{\"result\":{\"response\":null}}")]
    [InlineData("{\"result\":{\"choices\":[]}}")]
    public async Task An_empty_answer_is_reported(string reply)
    {
        var failure = await Record.ExceptionAsync(() => Ai(Replying(HttpStatusCode.OK, reply)).AskAsync("a", "b"));

        failure.Should().BeOfType<DomainException>().Which.Message.Should().Contain("no answer").And.Contain("Cloudflare said: ").And.Contain("result");
    }

    [Fact]
    public async Task The_days_free_allowance_being_spent_is_explained()
    {
        var failure = await Record.ExceptionAsync(() => Ai(Replying(HttpStatusCode.TooManyRequests, "{}")).AskAsync("a", "b"));

        failure.Should().BeOfType<DomainException>().Which.Message.Should().Contain("free allowance");
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized)]
    [InlineData(HttpStatusCode.Forbidden)]
    public async Task A_refused_token_is_explained_without_showing_it(HttpStatusCode status)
    {
        var failure = await Record.ExceptionAsync(() => Ai(Replying(status, "{}")).AskAsync("a", "b"));

        failure.Should().BeOfType<DomainException>().Which.Message.Should().Contain("Workers AI permission").And.NotContain("secret-token");
    }

    [Fact]
    public async Task A_network_failure_is_a_plain_message()
    {
        var failure = await Record.ExceptionAsync(() => Ai(new ThrowingHandler()).AskAsync("a", "b"));

        failure.Should().BeOfType<DomainException>().Which.Message.Should().Contain("could not be reached");
    }

    [Fact]
    public async Task A_reply_with_no_answer_in_it_is_reported()
    {
        var failure = await Record.ExceptionAsync(() => Ai(Replying(HttpStatusCode.OK, "{\"result\":{}}")).AskAsync("a", "b"));

        failure.Should().BeOfType<DomainException>().Which.Message.Should().Contain("no answer");
    }

    [Fact]
    public async Task A_wrong_account_id_is_caught_before_anything_is_sent()
    {
        var handler = Replying(HttpStatusCode.OK, "{}");

        var failure = await Record.ExceptionAsync(() => Ai(handler, new ImageGenerationOptions { AccountId = "short", ApiToken = "t" }).AskAsync("a", "b"));

        failure.Should().BeOfType<DomainException>();
        handler.Calls.Should().BeEmpty();
    }
}
