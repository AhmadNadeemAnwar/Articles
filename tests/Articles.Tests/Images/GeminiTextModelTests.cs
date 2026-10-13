using System.Net;
using System.Text.Json;
using FluentAssertions;
using Articles.Application.Posts;
using Articles.Domain.Exceptions;
using Articles.Infrastructure.Images;
using Articles.Tests.Security;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace Articles.Tests.Images;

public class GeminiTextModelTests
{
    private const string Key = "AIza-secret-key";

    private static GeminiTextModel Gemini(HttpMessageHandler handler, GeminiOptions? options = null) =>
        new(new HttpClient(handler), Options.Create(options ?? new GeminiOptions { ApiKey = Key }), NullLogger<GeminiTextModel>.Instance);

    private static StubHandler Replying(HttpStatusCode status, string body) =>
        new(_ => new HttpResponseMessage(status) { Content = new StringContent(body) });

    private static string Answer(string text) => JsonSerializer.Serialize(new { candidates = new[] { new { content = new { parts = new[] { new { text } } } } } });

    [Fact]
    public void It_is_switched_off_until_a_key_is_set()
    {
        Gemini(Replying(HttpStatusCode.OK, "{}"), new GeminiOptions()).IsEnabled.Should().BeFalse();
        Gemini(Replying(HttpStatusCode.OK, "{}"), new GeminiOptions { ApiKey = "  " }).IsEnabled.Should().BeFalse();
        Gemini(Replying(HttpStatusCode.OK, "{}")).IsEnabled.Should().BeTrue();
    }

    [Fact]
    public async Task The_request_goes_to_the_model_with_the_key_in_a_header_and_never_in_the_address()
    {
        var handler = Replying(HttpStatusCode.OK, Answer("[1] Better."));

        var answer = await Gemini(handler).CompleteAsync("be an editor", "[1] text", 800, 0.4);

        answer.Should().Be("[1] Better.");
        var (request, body) = handler.Calls.Single();
        request.RequestUri!.AbsoluteUri.Should().Be("https://generativelanguage.googleapis.com/v1beta/models/gemini-2.5-flash:generateContent");
        request.RequestUri.AbsoluteUri.Should().NotContain(Key);
        request.Headers.GetValues("x-goog-api-key").Should().Equal(Key);
        var json = JsonDocument.Parse(body).RootElement;
        json.GetProperty("systemInstruction").GetProperty("parts")[0].GetProperty("text").GetString().Should().Be("be an editor");
        json.GetProperty("contents")[0].GetProperty("parts")[0].GetProperty("text").GetString().Should().Be("[1] text");
        json.GetProperty("generationConfig").GetProperty("temperature").GetDouble().Should().Be(0.4);
    }

    [Fact]
    public async Task A_pasted_key_with_spaces_around_it_still_works()
    {
        var handler = Replying(HttpStatusCode.OK, Answer("ok"));

        await Gemini(handler, new GeminiOptions { ApiKey = $"  {Key}\n" }).CompleteAsync("a", "b", 100, 0.2);

        handler.Calls.Single().Request.Headers.GetValues("x-goog-api-key").Should().Equal(Key);
    }

    [Fact]
    public async Task Thinking_is_turned_off_for_2_5_models_so_it_cannot_use_up_the_answer_and_is_not_sent_to_others()
    {
        var two = Replying(HttpStatusCode.OK, Answer("ok"));
        await Gemini(two).CompleteAsync("a", "b", 100, 0.2);
        var config = JsonDocument.Parse(two.Calls.Single().Body).RootElement.GetProperty("generationConfig");
        config.GetProperty("thinkingConfig").GetProperty("thinkingBudget").GetInt32().Should().Be(0);
        config.GetProperty("maxOutputTokens").GetInt32().Should().BeGreaterThanOrEqualTo(1000);

        var other = Replying(HttpStatusCode.OK, Answer("ok"));
        await Gemini(other, new GeminiOptions { ApiKey = Key, Model = "gemini-2.0-flash" }).CompleteAsync("a", "b", 100, 0.2);
        JsonDocument.Parse(other.Calls.Single().Body).RootElement.GetProperty("generationConfig").TryGetProperty("thinkingConfig", out _).Should().BeFalse();
    }

    [Fact]
    public async Task The_model_can_be_changed_in_settings()
    {
        var handler = Replying(HttpStatusCode.OK, Answer("ok"));

        await Gemini(handler, new GeminiOptions { ApiKey = Key, Model = " gemini-2.5-flash-lite " }).CompleteAsync("a", "b", 100, 0.2);

        handler.Calls.Single().Request.RequestUri!.AbsoluteUri.Should().Contain("/models/gemini-2.5-flash-lite:generateContent");
    }

    [Fact]
    public async Task Pieces_of_a_split_answer_are_joined_and_thoughts_are_left_out()
    {
        var body = JsonSerializer.Serialize(new { candidates = new[] { new { content = new { parts = new object[] { new { text = "private thinking", thought = true }, new { text = "[1] One. " }, new { text = "[2] Two." } } } } } });

        (await Gemini(Replying(HttpStatusCode.OK, body)).CompleteAsync("a", "b", 100, 0.2)).Should().Be("[1] One. [2] Two.");
    }

    [Fact]
    public async Task Running_out_of_the_free_allowance_is_explained()
    {
        var failure = await Record.ExceptionAsync(() => Gemini(Replying(HttpStatusCode.TooManyRequests, "{}")).CompleteAsync("a", "b", 100, 0.2));

        failure.Should().BeOfType<DomainException>().Which.Message.Should().Contain("free allowance");
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized)]
    [InlineData(HttpStatusCode.Forbidden)]
    public async Task A_refused_key_is_explained_without_showing_it(HttpStatusCode status)
    {
        var failure = await Record.ExceptionAsync(() => Gemini(Replying(status, $"{{\"error\":{{\"message\":\"bad key {Key}\"}}}}")).CompleteAsync("a", "b", 100, 0.2));

        failure.Should().BeOfType<DomainException>().Which.Message.Should().Contain("key on the server").And.NotContain(Key);
    }

    [Fact]
    public async Task Another_failure_shows_googles_short_reason()
    {
        var failure = await Record.ExceptionAsync(() => Gemini(Replying(HttpStatusCode.BadRequest, "{\"error\":{\"message\":\"Unknown model\"}}")).CompleteAsync("a", "b", 100, 0.2));

        failure.Should().BeOfType<DomainException>().Which.Message.Should().Contain("400").And.Contain("Unknown model");
    }

    [Fact]
    public async Task A_blocked_or_empty_answer_is_reported()
    {
        var blocked = await Record.ExceptionAsync(() => Gemini(Replying(HttpStatusCode.OK, "{\"promptFeedback\":{\"blockReason\":\"SAFETY\"}}")).CompleteAsync("a", "b", 100, 0.2));
        var empty = await Record.ExceptionAsync(() => Gemini(Replying(HttpStatusCode.OK, "{\"candidates\":[]}")).CompleteAsync("a", "b", 100, 0.2));

        blocked.Should().BeOfType<DomainException>().Which.Message.Should().Contain("declined");
        empty.Should().BeOfType<DomainException>().Which.Message.Should().Contain("no answer");
    }

    [Fact]
    public async Task A_network_failure_is_a_plain_message()
    {
        var failure = await Record.ExceptionAsync(() => Gemini(new ThrowingHandler()).CompleteAsync("a", "b", 100, 0.2));

        failure.Should().BeOfType<DomainException>().Which.Message.Should().Contain("could not be reached");
    }
}
