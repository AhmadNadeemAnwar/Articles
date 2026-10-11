using System.Net;
using FluentAssertions;
using Articles.Domain.Exceptions;
using Articles.Infrastructure.Grammar;
using Articles.Tests.Security;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace Articles.Tests.Grammar;

public class LanguageToolCheckerTests
{
    private const string Reply = """
        { "matches": [
          { "message": "Possible spelling mistake found.", "offset": 4, "length": 5,
            "replacements": [ {"value":"quick"}, {"value":"quirk"}, {"value":""} ],
            "rule": { "category": { "name": "Possible Typo" } } },
          { "message": "Did you mean are?", "offset": 20, "length": 2, "replacements": [] }
        ] }
        """;

    private static LanguageToolChecker Checker(HttpMessageHandler handler, GrammarOptions? options = null) =>
        new(new HttpClient(handler), Options.Create(options ?? new GrammarOptions()), NullLogger<LanguageToolChecker>.Instance);

    private static StubHandler Replying(HttpStatusCode status, string body = "") =>
        new(_ => new HttpResponseMessage(status) { Content = new StringContent(body) });

    [Fact]
    public async Task Suggestions_are_read_from_the_services_reply()
    {
        var result = await Checker(Replying(HttpStatusCode.OK, Reply)).CheckAsync("The quikc brown fox is.");

        result.Matches.Should().HaveCount(2);
        var first = result.Matches[0];
        first.Offset.Should().Be(4);
        first.Length.Should().Be(5);
        first.Message.Should().Be("Possible spelling mistake found.");
        first.Category.Should().Be("Possible Typo");
        first.Replacements.Should().Equal("quick", "quirk");
    }

    [Fact]
    public async Task A_match_with_no_replacements_or_category_is_still_returned()
    {
        var result = await Checker(Replying(HttpStatusCode.OK, Reply)).CheckAsync("The quikc brown fox is.");

        result.Matches[1].Replacements.Should().BeEmpty();
        result.Matches[1].Category.Should().BeEmpty();
    }

    [Fact]
    public async Task At_most_five_suggestions_are_kept_for_each_match()
    {
        var many = string.Join(",", Enumerable.Range(1, 9).Select(i => $"{{\"value\":\"w{i}\"}}"));
        var body = $"{{\"matches\":[{{\"message\":\"m\",\"offset\":0,\"length\":1,\"replacements\":[{many}]}}]}}";

        var result = await Checker(Replying(HttpStatusCode.OK, body)).CheckAsync("x");

        result.Matches[0].Replacements.Should().HaveCount(5);
    }

    [Fact]
    public async Task Empty_text_is_not_sent_anywhere()
    {
        var handler = Replying(HttpStatusCode.OK, Reply);

        var result = await Checker(handler).CheckAsync("   ");

        result.Matches.Should().BeEmpty();
        handler.Calls.Should().BeEmpty();
    }

    [Fact]
    public async Task The_text_and_language_are_posted_to_the_check_endpoint()
    {
        var handler = Replying(HttpStatusCode.OK, "{\"matches\":[]}");

        await Checker(handler, new GrammarOptions { BaseUrl = "https://lt.example/", Language = "en-GB" }).CheckAsync("Hello & welcome");

        var (request, body) = handler.Calls.Single();
        request.Method.Should().Be(HttpMethod.Post);
        request.RequestUri!.ToString().Should().Be("https://lt.example/v2/check");
        body.Should().Contain("language=en-GB").And.Contain("text=Hello+%26+welcome");
    }

    [Fact]
    public async Task A_busy_service_gives_a_plain_message()
    {
        var act = () => Checker(Replying(HttpStatusCode.TooManyRequests)).CheckAsync("Hello");

        (await act.Should().ThrowAsync<DomainException>()).WithMessage("*busy*");
    }

    [Fact]
    public async Task A_service_error_gives_a_plain_message()
    {
        var act = () => Checker(Replying(HttpStatusCode.InternalServerError)).CheckAsync("Hello");

        await act.Should().ThrowAsync<DomainException>();
    }

    [Fact]
    public async Task A_network_failure_gives_a_plain_message()
    {
        var act = () => Checker(new ThrowingHandler()).CheckAsync("Hello");

        (await act.Should().ThrowAsync<DomainException>()).WithMessage("*could not be reached*");
    }

    [Fact]
    public async Task A_reply_that_is_not_json_gives_a_plain_message()
    {
        var act = () => Checker(Replying(HttpStatusCode.OK, "<html>oops</html>")).CheckAsync("Hello");

        await act.Should().ThrowAsync<DomainException>();
    }
}
