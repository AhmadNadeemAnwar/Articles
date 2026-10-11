using System.Net;
using System.Text.Json;
using FluentAssertions;
using Articles.Domain.Exceptions;
using Articles.Infrastructure.Images;
using Articles.Tests.Security;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace Articles.Tests.Images;

public class CloudflareImageGeneratorTests
{
    private const string ValidId = "9abb8165ab250861d49f8f0f289a0823";
    private static readonly byte[] Picture = [0xFF, 0xD8, 0xFF, 0xE0, 1, 2, 3];
    private static readonly string Base64 = Convert.ToBase64String(Picture);

    private static ImageGenerationOptions Configured() => new() { AccountId = ValidId, ApiToken = "secret-token" };

    private static CloudflareImageGenerator Generator(HttpMessageHandler handler, ImageGenerationOptions? options = null) =>
        new(new HttpClient(handler), Options.Create(options ?? Configured()), NullLogger<CloudflareImageGenerator>.Instance);

    private static StubHandler Replying(HttpStatusCode status, string body) =>
        new(_ => new HttpResponseMessage(status) { Content = new StringContent(body) });

    private static StubHandler Succeeding() => Replying(HttpStatusCode.OK, $"{{\"result\":{{\"image\":\"{Base64}\"}},\"success\":true,\"errors\":[]}}");

    [Fact]
    public async Task It_is_switched_off_until_both_the_account_and_the_token_are_set()
    {
        Generator(Succeeding(), new ImageGenerationOptions()).IsEnabled.Should().BeFalse();
        Generator(Succeeding(), new ImageGenerationOptions { AccountId = "a" }).IsEnabled.Should().BeFalse();
        Generator(Succeeding(), new ImageGenerationOptions { ApiToken = "t" }).IsEnabled.Should().BeFalse();
        Generator(Succeeding()).IsEnabled.Should().BeTrue();
    }

    [Fact]
    public async Task The_picture_is_decoded_from_the_wrapped_reply()
    {
        (await Generator(Succeeding()).GenerateAsync("a lighthouse")).Should().Equal(Picture);
    }

    [Fact]
    public async Task A_reply_without_the_wrapper_is_read_too()
    {
        var handler = Replying(HttpStatusCode.OK, $"{{\"image\":\"{Base64}\"}}");

        (await Generator(handler).GenerateAsync("a lighthouse")).Should().Equal(Picture);
    }

    [Fact]
    public async Task The_request_goes_to_the_accounts_model_with_the_token_and_prompt()
    {
        var handler = Succeeding();

        await Generator(handler).GenerateAsync("a \"quoted\" lighthouse");

        var (request, body) = handler.Calls.Single();
        request.Method.Should().Be(HttpMethod.Post);
        request.RequestUri!.AbsoluteUri.Should().Be("https://api.cloudflare.com/client/v4/accounts/"+ValidId+"/ai/run/@cf/black-forest-labs/flux-1-schnell");
        request.Headers.Authorization!.ToString().Should().Be("Bearer secret-token");
        var json = JsonDocument.Parse(body).RootElement;
        json.GetProperty("prompt").GetString().Should().Be("a \"quoted\" lighthouse");
        json.GetProperty("steps").GetInt32().Should().Be(4);
    }

    [Theory]
    [InlineData(0, 1)]
    [InlineData(50, 8)]
    [InlineData(6, 6)]
    public async Task The_step_count_stays_within_what_the_model_allows(int configured, int sent)
    {
        var handler = Succeeding();
        var options = Configured();
        options.Steps = configured;

        await Generator(handler, options).GenerateAsync("x y z");

        JsonDocument.Parse(handler.Calls.Single().Body).RootElement.GetProperty("steps").GetInt32().Should().Be(sent);
    }

    [Fact]
    public async Task Running_out_of_the_free_allowance_is_explained_in_plain_words()
    {
        foreach (var handler in new[]
        {
            Replying(HttpStatusCode.TooManyRequests, "{}"),
            Replying(HttpStatusCode.BadRequest, "{\"errors\":[{\"code\":4006,\"message\":\"you have used up your daily free allocation of 10,000 neurons\"}]}")
        })
        {
            var act = () => Generator(handler).GenerateAsync("a lighthouse");
            (await act.Should().ThrowAsync<DomainException>()).WithMessage("*free allowance*tomorrow*");
        }
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized)]
    [InlineData(HttpStatusCode.Forbidden)]
    public async Task A_refused_token_points_at_the_setup_steps_without_repeating_the_token(HttpStatusCode status)
    {
        var act = () => Generator(Replying(status, "{}")).GenerateAsync("a lighthouse");

        var thrown = (await act.Should().ThrowAsync<DomainException>()).Which;
        thrown.Message.Should().Contain("ADMIN.md").And.NotContain("secret-token");
    }

    [Fact]
    public async Task Any_other_error_gives_a_plain_message()
    {
        var act = () => Generator(Replying(HttpStatusCode.InternalServerError, "boom")).GenerateAsync("a lighthouse");

        (await act.Should().ThrowAsync<DomainException>()).WithMessage("*could not make that picture*");
    }

    [Fact]
    public async Task Spaces_and_line_breaks_pasted_around_the_settings_are_ignored()
    {
        var handler = Succeeding();
        var options = new ImageGenerationOptions { AccountId = "\n" + ValidId + " ", ApiToken = " secret-token\r\n" };

        await Generator(handler, options).GenerateAsync("a lighthouse");

        var (request, _) = handler.Calls.Single();
        request.RequestUri!.AbsoluteUri.Should().Contain("/accounts/" + ValidId + "/ai/run");
        request.Headers.Authorization!.ToString().Should().Be("Bearer secret-token");
    }

    [Theory]
    [InlineData("9abb8165ab250861d49f8f0f289a082")]       // one character short
    [InlineData("9abb8165ab250861d49f8f0f289a08234")]     // one too long
    [InlineData("zabb8165ab250861d49f8f0f289a0823")]      // not hexadecimal
    public async Task An_account_id_of_the_wrong_shape_is_explained_and_never_sent(string id)
    {
        var handler = Succeeding();

        var act = () => Generator(handler, new ImageGenerationOptions { AccountId = id, ApiToken = "secret-token" }).GenerateAsync("a lighthouse");

        (await act.Should().ThrowAsync<DomainException>()).WithMessage("*account ID*32*");
        handler.Calls.Should().BeEmpty();
    }

    [Fact]
    public async Task The_message_includes_what_Cloudflare_said_so_a_setup_mistake_can_be_found()
    {
        var handler = Replying(HttpStatusCode.NotFound, "{\"success\":false,\"errors\":[{\"code\":7003,\"message\":\"Could not route to /accounts/"+ValidId+"/ai/run\"}]}");

        var act = () => Generator(handler).GenerateAsync("a lighthouse");

        var thrown = (await act.Should().ThrowAsync<DomainException>()).Which;
        thrown.Message.Should().Contain("404").And.Contain("Could not route").And.NotContain("secret-token");
    }

    [Fact]
    public async Task A_very_long_Cloudflare_explanation_is_cut_short()
    {
        var handler = Replying(HttpStatusCode.BadRequest, "{\"errors\":[{\"message\":\"" + new string('x', 1000) + "\"}]}");

        var act = () => Generator(handler).GenerateAsync("a lighthouse");

        (await act.Should().ThrowAsync<DomainException>()).Which.Message.Length.Should().BeLessThan(400);
    }

    [Fact]
    public async Task A_network_failure_gives_a_plain_message()
    {
        var act = () => Generator(new ThrowingHandler()).GenerateAsync("a lighthouse");

        (await act.Should().ThrowAsync<DomainException>()).WithMessage("*could not be reached*");
    }

    [Theory]
    [InlineData("<html>oops</html>")]
    [InlineData("{\"result\":{},\"success\":true}")]
    [InlineData("{\"result\":{\"image\":\"%%%not base64%%%\"}}")]
    public async Task A_reply_with_no_usable_picture_gives_a_plain_message(string body)
    {
        var act = () => Generator(Replying(HttpStatusCode.OK, body)).GenerateAsync("a lighthouse");

        await act.Should().ThrowAsync<DomainException>();
    }
}
