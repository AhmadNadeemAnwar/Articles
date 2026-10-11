using System.Net;
using System.Security.Cryptography;
using System.Text;
using FluentAssertions;
using Articles.Infrastructure.Security;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace Articles.Tests.Security;

/// <summary>Stands in for the network: returns a canned response and records what was sent.</summary>
internal sealed class StubHandler : HttpMessageHandler
{
    private readonly Func<HttpRequestMessage, HttpResponseMessage> _respond;
    public List<(HttpRequestMessage Request, string Body)> Calls { get; } = new();

    public StubHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) => _respond = respond;

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var body = request.Content is null ? "" : await request.Content.ReadAsStringAsync(cancellationToken);
        Calls.Add((request, body));
        return _respond(request);
    }
}

internal sealed class ThrowingHandler : HttpMessageHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
        throw new HttpRequestException("network down");
}

public class PwnedPasswordCheckerTests
{
    private static string Sha1(string value) => Convert.ToHexString(SHA1.HashData(Encoding.UTF8.GetBytes(value)));

    private static PwnedPasswordChecker Checker(HttpMessageHandler handler) =>
        new(new HttpClient(handler), NullLogger<PwnedPasswordChecker>.Instance);

    [Fact]
    public async Task A_password_whose_hash_suffix_appears_with_a_positive_count_is_pwned()
    {
        var hash = Sha1("hunter2-breached");
        var handler = new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent($"0000000000000000000000000000000000A:0\r\n{hash[5..]}:42\r\nFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFF:3")
        });

        (await Checker(handler).IsPwnedAsync("hunter2-breached")).Should().BeTrue();
    }

    [Fact]
    public async Task Only_the_first_five_hash_characters_ever_leave_the_server()
    {
        var hash = Sha1("a-private-password");
        var handler = new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("") });

        await Checker(handler).IsPwnedAsync("a-private-password");

        var url = handler.Calls.Single().Request.RequestUri!.ToString();
        url.Should().EndWith($"/range/{hash[..5]}");
        url.Should().NotContain(hash[5..], "the rest of the hash must stay private");
        url.Should().NotContain("a-private-password");
    }

    [Fact]
    public async Task Padding_rows_with_a_zero_count_are_not_treated_as_matches()
    {
        var hash = Sha1("padding-case");
        var handler = new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent($"{hash[5..]}:0")
        });

        (await Checker(handler).IsPwnedAsync("padding-case")).Should().BeFalse();
    }

    [Fact]
    public async Task An_unlisted_password_is_not_pwned()
    {
        var handler = new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA:9")
        });

        (await Checker(handler).IsPwnedAsync("a-fresh-unseen-passphrase")).Should().BeFalse();
    }

    [Fact]
    public async Task The_check_fails_open_when_the_service_is_unreachable() =>
        (await Checker(new ThrowingHandler()).IsPwnedAsync("anything-at-all")).Should().BeFalse();

    [Fact]
    public async Task The_check_fails_open_on_an_error_response() =>
        (await Checker(new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)))
            .IsPwnedAsync("anything-at-all")).Should().BeFalse();
}

public class TurnstileVerifierTests
{
    private static TurnstileVerifier Verifier(HttpMessageHandler handler, string secret = "secret-key") =>
        new(new HttpClient(handler), Options.Create(new TurnstileOptions { SecretKey = secret }), NullLogger<TurnstileVerifier>.Instance);

    private static HttpResponseMessage Json(string body, HttpStatusCode status = HttpStatusCode.OK) =>
        new(status) { Content = new StringContent(body) };

    [Fact]
    public async Task It_is_disabled_without_a_secret_and_always_passes()
    {
        var verifier = Verifier(new ThrowingHandler(), secret: "");

        verifier.IsEnabled.Should().BeFalse();
        (await verifier.VerifyAsync(null)).Should().BeTrue();
    }

    [Fact]
    public async Task A_successful_verification_passes_and_sends_the_secret_and_token()
    {
        var handler = new StubHandler(_ => Json("{\"success\":true}"));

        (await Verifier(handler).VerifyAsync("widget-token")).Should().BeTrue();

        var body = handler.Calls.Single().Body;
        body.Should().Contain("secret=secret-key").And.Contain("response=widget-token");
    }

    [Fact]
    public async Task A_rejected_token_fails() =>
        (await Verifier(new StubHandler(_ => Json("{\"success\":false,\"error-codes\":[\"invalid-input-response\"]}")))
            .VerifyAsync("forged")).Should().BeFalse();

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task A_missing_token_fails_without_calling_cloudflare(string? token)
    {
        var handler = new StubHandler(_ => Json("{\"success\":true}"));

        (await Verifier(handler).VerifyAsync(token)).Should().BeFalse();
        handler.Calls.Should().BeEmpty();
    }

    [Fact]
    public async Task When_enabled_it_fails_closed_if_cloudflare_cannot_be_reached() =>
        (await Verifier(new ThrowingHandler()).VerifyAsync("token")).Should().BeFalse();

    [Fact]
    public async Task A_garbled_response_fails_closed() =>
        (await Verifier(new StubHandler(_ => Json("<html>oops</html>"))).VerifyAsync("token")).Should().BeFalse();
}

public class LoginAttemptTrackerTests
{
    [Fact]
    public void An_account_locks_after_the_maximum_number_of_failures()
    {
        var tracker = new InMemoryLoginAttemptTracker(new ManualTimeProvider());

        for (var i = 0; i < InMemoryLoginAttemptTracker.MaxFailures - 1; i++) tracker.RecordFailure("a");
        tracker.IsLockedOut("a").Should().BeFalse();

        tracker.RecordFailure("a");
        tracker.IsLockedOut("a").Should().BeTrue();
    }

    [Fact]
    public void Failures_age_out_so_the_lock_lifts_by_itself()
    {
        var clock = new ManualTimeProvider();
        var tracker = new InMemoryLoginAttemptTracker(clock);
        for (var i = 0; i < InMemoryLoginAttemptTracker.MaxFailures; i++) tracker.RecordFailure("a");
        tracker.IsLockedOut("a").Should().BeTrue();

        clock.Advance(InMemoryLoginAttemptTracker.Window + TimeSpan.FromSeconds(1));

        tracker.IsLockedOut("a").Should().BeFalse();
    }

    [Fact]
    public void The_window_slides_rather_than_resetting_all_at_once()
    {
        var clock = new ManualTimeProvider();
        var tracker = new InMemoryLoginAttemptTracker(clock);

        for (var i = 0; i < 6; i++) tracker.RecordFailure("a");
        clock.Advance(TimeSpan.FromMinutes(10));
        for (var i = 0; i < 4; i++) tracker.RecordFailure("a");
        tracker.IsLockedOut("a").Should().BeTrue("ten failures fall inside one 15-minute window");

        clock.Advance(TimeSpan.FromMinutes(6));
        tracker.IsLockedOut("a").Should().BeFalse("the first six have aged out, leaving four");
    }

    [Fact]
    public void Clearing_removes_the_failures_and_keys_are_independent()
    {
        var tracker = new InMemoryLoginAttemptTracker(new ManualTimeProvider());
        for (var i = 0; i < InMemoryLoginAttemptTracker.MaxFailures; i++)
        {
            tracker.RecordFailure("a");
            tracker.RecordFailure("b");
        }

        tracker.Clear("a");

        tracker.IsLockedOut("a").Should().BeFalse();
        tracker.IsLockedOut("b").Should().BeTrue();
    }
}
