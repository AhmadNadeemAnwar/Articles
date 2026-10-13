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

public class GroqTextModelTests
{
    private const string Key = "gsk_secret-key";

    private static GroqTextModel Groq(HttpMessageHandler handler, GroqOptions? options = null) =>
        new(new HttpClient(handler), Options.Create(options ?? new GroqOptions { ApiKey = Key }), NullLogger<GroqTextModel>.Instance);

    private static StubHandler Replying(HttpStatusCode status, string body) =>
        new(_ => new HttpResponseMessage(status) { Content = new StringContent(body) });

    private static string Answer(string text) => JsonSerializer.Serialize(new { choices = new[] { new { message = new { role = "assistant", content = text } } } });

    [Fact]
    public void It_is_switched_off_until_a_key_is_set()
    {
        Groq(Replying(HttpStatusCode.OK, "{}"), new GroqOptions()).IsEnabled.Should().BeFalse();
        Groq(Replying(HttpStatusCode.OK, "{}"), new GroqOptions { ApiKey = " " }).IsEnabled.Should().BeFalse();
        Groq(Replying(HttpStatusCode.OK, "{}")).IsEnabled.Should().BeTrue();
    }

    [Fact]
    public async Task The_request_goes_to_the_chat_endpoint_with_the_key_as_a_bearer_token()
    {
        var handler = Replying(HttpStatusCode.OK, Answer("[1] Better."));

        var answer = await Groq(handler).CompleteAsync("be an editor", "[1] text", 800, 0.4);

        answer.Should().Be("[1] Better.");
        var (request, body) = handler.Calls.Single();
        request.RequestUri!.AbsoluteUri.Should().Be("https://api.groq.com/openai/v1/chat/completions");
        request.Headers.Authorization!.ToString().Should().Be($"Bearer {Key}");
        var json = JsonDocument.Parse(body).RootElement;
        json.GetProperty("model").GetString().Should().Be("llama-3.3-70b-versatile");
        json.GetProperty("temperature").GetDouble().Should().Be(0.4);
        json.GetProperty("max_tokens").GetInt32().Should().BeGreaterThanOrEqualTo(1000);
        var messages = json.GetProperty("messages");
        messages[0].GetProperty("role").GetString().Should().Be("system");
        messages[0].GetProperty("content").GetString().Should().Be("be an editor");
        messages[1].GetProperty("content").GetString().Should().Be("[1] text");
    }

    [Fact]
    public async Task The_model_and_address_can_be_changed_in_settings_so_other_services_work_too()
    {
        var handler = Replying(HttpStatusCode.OK, Answer("ok"));

        await Groq(handler, new GroqOptions { ApiKey = $" {Key}\n", Model = " mistral-small-latest ", BaseUrl = "https://api.mistral.ai/v1/" }).CompleteAsync("a", "b", 100, 0.2);

        var (request, body) = handler.Calls.Single();
        request.RequestUri!.AbsoluteUri.Should().Be("https://api.mistral.ai/v1/chat/completions");
        request.Headers.Authorization!.ToString().Should().Be($"Bearer {Key}");
        JsonDocument.Parse(body).RootElement.GetProperty("model").GetString().Should().Be("mistral-small-latest");
    }

    [Fact]
    public async Task Running_out_of_the_free_allowance_is_explained()
    {
        var failure = await Record.ExceptionAsync(() => Groq(Replying(HttpStatusCode.TooManyRequests, "{}")).CompleteAsync("a", "b", 100, 0.2));

        failure.Should().BeOfType<DomainException>().Which.Message.Should().Contain("free allowance");
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized)]
    [InlineData(HttpStatusCode.Forbidden)]
    public async Task A_refused_key_is_explained_without_showing_it(HttpStatusCode status)
    {
        var failure = await Record.ExceptionAsync(() => Groq(Replying(status, $"{{\"error\":{{\"message\":\"bad key {Key}\"}}}}")).CompleteAsync("a", "b", 100, 0.2));

        failure.Should().BeOfType<DomainException>().Which.Message.Should().Contain("key on the server").And.NotContain(Key);
    }

    [Fact]
    public async Task Another_failure_shows_the_services_short_reason()
    {
        var failure = await Record.ExceptionAsync(() => Groq(Replying(HttpStatusCode.BadRequest, "{\"error\":{\"message\":\"The model does not exist\"}}")).CompleteAsync("a", "b", 100, 0.2));

        failure.Should().BeOfType<DomainException>().Which.Message.Should().Contain("400").And.Contain("does not exist");
    }

    [Theory]
    [InlineData("{\"choices\":[]}")]
    [InlineData("{\"choices\":[{\"message\":{\"content\":\"\"}}]}")]
    [InlineData("{}")]
    public async Task An_empty_answer_is_reported(string body)
    {
        var failure = await Record.ExceptionAsync(() => Groq(Replying(HttpStatusCode.OK, body)).CompleteAsync("a", "b", 100, 0.2));

        failure.Should().BeOfType<DomainException>().Which.Message.Should().Contain("no answer");
    }

    [Fact]
    public async Task A_network_failure_is_a_plain_message()
    {
        var failure = await Record.ExceptionAsync(() => Groq(new ThrowingHandler()).CompleteAsync("a", "b", 100, 0.2));

        failure.Should().BeOfType<DomainException>().Which.Message.Should().Contain("could not be reached");
    }
}

public class ChainedTextModelTests
{
    private sealed class Fake(string name, bool enabled = true, bool fails = false) : ITextModel
    {
        public bool IsEnabled { get; } = enabled;
        public int Calls { get; private set; }

        public Task<string> CompleteAsync(string instruction, string request, int maxTokens, double temperature, CancellationToken ct = default)
        {
            Calls++;
            return fails ? throw new DomainException($"{name} is out of allowance") : Task.FromResult($"from {name}");
        }
    }

    private static ChainedTextModel Chain(params ITextModel[] models) => new(models, NullLogger<ChainedTextModel>.Instance);

    [Fact]
    public async Task The_first_that_is_set_up_answers_and_the_others_are_not_asked()
    {
        var (gemini, groq, cloudflare) = (new Fake("gemini"), new Fake("groq"), new Fake("cloudflare"));

        (await Chain(gemini, groq, cloudflare).CompleteAsync("a", "b", 100, 0.2)).Should().Be("from gemini");

        (groq.Calls, cloudflare.Calls).Should().Be((0, 0));
    }

    [Fact]
    public async Task One_that_is_not_set_up_is_skipped()
    {
        var (gemini, groq, cloudflare) = (new Fake("gemini", enabled: false), new Fake("groq"), new Fake("cloudflare"));

        (await Chain(gemini, groq, cloudflare).CompleteAsync("a", "b", 100, 0.2)).Should().Be("from groq");

        gemini.Calls.Should().Be(0);
    }

    [Fact]
    public async Task When_one_cannot_answer_the_next_does()
    {
        var (gemini, groq, cloudflare) = (new Fake("gemini", fails: true), new Fake("groq", fails: true), new Fake("cloudflare"));

        (await Chain(gemini, groq, cloudflare).CompleteAsync("a", "b", 100, 0.2)).Should().Be("from cloudflare");

        (gemini.Calls, groq.Calls, cloudflare.Calls).Should().Be((1, 1, 1));
    }

    [Fact]
    public async Task When_every_one_fails_the_last_reason_is_given()
    {
        var failure = await Record.ExceptionAsync(() => Chain(new Fake("gemini", fails: true), new Fake("groq", fails: true)).CompleteAsync("a", "b", 100, 0.2));

        failure.Should().BeOfType<DomainException>().Which.Message.Should().Be("groq is out of allowance");
    }

    [Fact]
    public void The_feature_is_on_when_any_is_set_up_and_off_when_none_is()
    {
        Chain(new Fake("gemini", enabled: false), new Fake("groq")).IsEnabled.Should().BeTrue();
        Chain(new Fake("gemini", enabled: false), new Fake("groq", enabled: false)).IsEnabled.Should().BeFalse();
        Chain().IsEnabled.Should().BeFalse();
    }

    [Fact]
    public async Task With_none_set_up_asking_says_so()
    {
        var failure = await Record.ExceptionAsync(() => Chain(new Fake("gemini", enabled: false)).CompleteAsync("a", "b", 100, 0.2));

        failure.Should().BeOfType<DomainException>().Which.Message.Should().Contain("not set up");
    }
}
