using FluentAssertions;
using Articles.Application.Admin;
using Articles.Application.Admin.Dtos;
using Articles.Application.Auth;
using Articles.Application.Auth.Dtos;
using Articles.Application.Common;
using Articles.Domain.Entities;
using Articles.Domain.Exceptions;
using Articles.Infrastructure.Security;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using OtpNet;
using Xunit;

namespace Articles.Tests.Admin;

/// <summary>An options monitor the test can change mid-run, to prove configuration is read live.</summary>
public sealed class TestOptionsMonitor<T> : IOptionsMonitor<T> where T : class
{
    public TestOptionsMonitor(T value) => CurrentValue = value;
    public T CurrentValue { get; set; }
    public T Get(string? name) => CurrentValue;
    public IDisposable? OnChange(Action<T, string?> listener) => null;
}

public static class TotpTestSecret
{
    public static string NewSecret() => Base32Encoding.ToString(KeyGeneration.GenerateRandomKey(20));

    public static string CodeFor(string base32Secret, DateTimeOffset at) =>
        new Totp(Base32Encoding.ToBytes(base32Secret)).ComputeTotp(at.UtcDateTime);
}

public class TotpVerifierTests
{
    private readonly string _secret = TotpTestSecret.NewSecret();
    private readonly ManualTimeProvider _clock = new(new DateTimeOffset(2026, 6, 1, 12, 0, 10, TimeSpan.Zero));

    private TotpVerifier Verifier(string? secret = null) =>
        new(new TestOptionsMonitor<AdminOptions>(new AdminOptions { TotpSecret = secret ?? _secret }), _clock);

    [Fact]
    public void The_current_code_is_accepted()
    {
        var code = TotpTestSecret.CodeFor(_secret, _clock.GetUtcNow());

        Verifier().Verify(code).Should().NotBeNull();
    }

    [Fact]
    public void A_code_from_the_previous_step_is_accepted_to_allow_for_clock_drift()
    {
        var code = TotpTestSecret.CodeFor(_secret, _clock.GetUtcNow().AddSeconds(-30));

        Verifier().Verify(code).Should().NotBeNull();
    }

    [Fact]
    public void A_code_from_two_steps_ago_is_rejected()
    {
        var code = TotpTestSecret.CodeFor(_secret, _clock.GetUtcNow().AddSeconds(-90));

        Verifier().Verify(code).Should().BeNull();
    }

    [Theory]
    [InlineData("000000")]
    [InlineData("123456")]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("abcdef")]
    public void Wrong_or_malformed_codes_are_rejected(string code) =>
        Verifier().Verify(code).Should().BeNull();

    [Fact]
    public void A_code_cannot_be_used_twice()
    {
        var verifier = Verifier();
        var code = TotpTestSecret.CodeFor(_secret, _clock.GetUtcNow());

        var step = verifier.Verify(code);
        step.Should().NotBeNull();
        verifier.Consume(step!.Value);

        verifier.Verify(code).Should().BeNull("the same code must not work again inside its window");
    }

    [Fact]
    public void A_code_that_was_verified_but_not_consumed_still_works()
    {
        // Sign-in only consumes a code once every factor passed, so a mistyped password does not burn it.
        var verifier = Verifier();
        var code = TotpTestSecret.CodeFor(_secret, _clock.GetUtcNow());

        verifier.Verify(code).Should().NotBeNull();
        verifier.Verify(code).Should().NotBeNull();
    }

    [Fact]
    public void A_later_code_still_works_after_an_earlier_one_was_spent()
    {
        var verifier = Verifier();
        verifier.Consume(verifier.Verify(TotpTestSecret.CodeFor(_secret, _clock.GetUtcNow()))!.Value);

        _clock.Advance(TimeSpan.FromSeconds(30));

        verifier.Verify(TotpTestSecret.CodeFor(_secret, _clock.GetUtcNow())).Should().NotBeNull();
    }

    [Theory]
    [InlineData("")]
    [InlineData("not base32 !!!")]
    [InlineData("AAAA")]
    public void A_missing_or_unusable_secret_accepts_nothing(string secret)
    {
        var code = TotpTestSecret.CodeFor(_secret, _clock.GetUtcNow());

        Verifier(secret).Verify(code).Should().BeNull();
    }

    [Fact]
    public void The_secret_may_be_typed_in_lower_case_with_spaces()
    {
        var spaced = string.Join(' ', Enumerable.Range(0, _secret.Length / 4 + 1).Select(i => _secret.Substring(i * 4, Math.Min(4, _secret.Length - i * 4))).Where(x => x.Length > 0)).ToLowerInvariant();
        var code = TotpTestSecret.CodeFor(_secret, _clock.GetUtcNow());

        Verifier(spaced).Verify(code).Should().NotBeNull();
    }
}

public class ConfiguredAdminDirectoryTests
{
    [Fact]
    public void Email_matching_ignores_case_and_whitespace()
    {
        var directory = new ConfiguredAdminDirectory(new TestOptionsMonitor<AdminOptions>(
            new AdminOptions { Emails = [" Owner@Example.com "], TotpSecret = TotpTestSecret.NewSecret() }));

        directory.IsAdmin("owner@example.com").Should().BeTrue();
        directory.IsAdmin("OWNER@EXAMPLE.COM ").Should().BeTrue();
        directory.IsAdmin("someone@example.com").Should().BeFalse();
        directory.IsAdmin("").Should().BeFalse();
    }

    [Fact]
    public void It_is_not_configured_without_both_an_email_and_a_valid_secret()
    {
        bool Configured(string[] emails, string secret) =>
            new ConfiguredAdminDirectory(new TestOptionsMonitor<AdminOptions>(new AdminOptions { Emails = emails, TotpSecret = secret })).IsConfigured;

        Configured([], TotpTestSecret.NewSecret()).Should().BeFalse();
        Configured(["a@b.co"], "").Should().BeFalse();
        Configured(["a@b.co"], "not a secret!").Should().BeFalse();
        Configured([" "], TotpTestSecret.NewSecret()).Should().BeFalse();
        Configured(["a@b.co"], TotpTestSecret.NewSecret()).Should().BeTrue();
    }

    [Fact]
    public void Changes_to_the_list_take_effect_immediately()
    {
        var monitor = new TestOptionsMonitor<AdminOptions>(new AdminOptions { Emails = ["owner@example.com"], TotpSecret = TotpTestSecret.NewSecret() });
        var directory = new ConfiguredAdminDirectory(monitor);
        directory.IsAdmin("owner@example.com").Should().BeTrue();

        monitor.CurrentValue = new AdminOptions { Emails = ["someone-else@example.com"], TotpSecret = monitor.CurrentValue.TotpSecret };

        directory.IsAdmin("owner@example.com").Should().BeFalse("removing an admin must not wait for their token to expire");
    }
}

public class AdminAuthServiceTests : IDisposable
{
    private const string Email = "owner@example.com";
    private const string Password = "a password that admin sign-in never asks for";

    private readonly TestDatabase _fixture = new();
    private readonly string _secret = TotpTestSecret.NewSecret();
    private readonly ManualTimeProvider _clock = new(new DateTimeOffset(2026, 6, 1, 12, 0, 10, TimeSpan.Zero));
    private readonly TestOptionsMonitor<AdminOptions> _options;
    private readonly AdminAuthService _service;
    private readonly Mock<ITokenService> _tokens = new();

    public AdminAuthServiceTests()
    {
        _options = new TestOptionsMonitor<AdminOptions>(new AdminOptions { Emails = [Email], TotpSecret = _secret });
        _tokens.Setup(t => t.CreateAdminSession(It.IsAny<User>()))
            .Returns(new AccessToken("admin-session-token", DateTimeOffset.UtcNow.AddHours(2)));

        _service = new AdminAuthService(
            _fixture.Users,
            _tokens.Object,
            new ConfiguredAdminDirectory(_options),
            new TotpVerifier(_options, _clock),
            new AdminLoginAttemptTracker(_clock),
            NullLogger<AdminAuthService>.Instance);

        _fixture.Db.Users.Add(new User(Email, "the-owner", "The Owner", new FakePasswordHasher().Hash(Password)));
        _fixture.Db.Users.Add(new User("reader@example.com", "a-reader", "A Reader", new FakePasswordHasher().Hash(Password)));
        _fixture.Db.SaveChanges();
    }

    private string Code(int secondsFromNow = 0) => TotpTestSecret.CodeFor(_secret, _clock.GetUtcNow().AddSeconds(secondsFromNow));

    private AdminLoginRequest Login(string email = Email, string? code = null) =>
        new(email, code ?? Code());

    [Fact]
    public async Task The_right_email_and_code_open_an_admin_session()
    {
        var session = await _service.LoginAsync(Login());

        session.Token.Should().Be("admin-session-token");
        session.Email.Should().Be(Email);
        _tokens.Verify(t => t.CreateAdminSession(It.Is<User>(u => u.Email == Email)), Times.Once);
    }

    [Fact]
    public async Task The_email_is_matched_without_regard_to_case()
    {
        (await _service.LoginAsync(Login(email: "  OWNER@Example.COM "))).Email.Should().Be(Email);
    }

    public static IEnumerable<object[]> BadCredentials() =>
    [
        ["wrong code"],
        ["not an admin"],
        ["unknown email"],
    ];

    [Theory]
    [MemberData(nameof(BadCredentials))]
    public async Task Any_wrong_detail_fails_with_the_same_message(string what)
    {
        var request = what switch
        {
            "wrong code" => Login(code: "000000"),
            "not an admin" => Login(email: "reader@example.com"),
            _ => Login(email: "nobody@example.com"),
        };

        var failure = await Record.ExceptionAsync(() => _service.LoginAsync(request));

        failure.Should().BeOfType<DomainException>();
        failure!.Message.Should().Be("Invalid email or code.", "the message must not reveal which part was wrong");
        _tokens.Verify(t => t.CreateAdminSession(It.IsAny<User>()), Times.Never);
    }

    [Fact]
    public async Task A_valid_code_for_an_account_that_is_not_an_admin_is_refused()
    {
        // The account exists and the code is valid, but the email is not on the list.
        var failure = await Record.ExceptionAsync(() => _service.LoginAsync(Login(email: "reader@example.com")));

        failure.Should().BeOfType<DomainException>();
    }

    [Fact]
    public async Task A_code_that_was_just_used_cannot_be_replayed()
    {
        var code = Code();
        await _service.LoginAsync(Login(code: code));

        var replay = await Record.ExceptionAsync(() => _service.LoginAsync(Login(code: code)));

        replay.Should().BeOfType<DomainException>();
    }

    [Fact]
    public async Task A_mistyped_email_does_not_burn_the_code()
    {
        var code = Code();
        await Record.ExceptionAsync(() => _service.LoginAsync(Login(email: "ownr@example.com", code: code)));

        // Same 30-second window, correct email this time: it must still work.
        (await _service.LoginAsync(Login(code: code))).Token.Should().Be("admin-session-token");
    }

    [Fact]
    public void A_password_is_not_part_of_admin_sign_in()
    {
        typeof(AdminLoginRequest).GetProperties().Select(p => p.Name).Should().BeEquivalentTo(["Email", "Code"]);
    }

    [Fact]
    public async Task Four_wrong_codes_still_leave_room_for_the_right_one()
    {
        for (var i = 0; i < AdminLoginAttemptTracker.MaxFailures - 1; i++)
            await Record.ExceptionAsync(() => _service.LoginAsync(Login(code: "000000")));

        (await _service.LoginAsync(Login())).Token.Should().Be("admin-session-token");
    }

    [Fact]
    public async Task The_lock_is_still_in_place_after_the_ordinary_fifteen_minutes()
    {
        for (var i = 0; i < AdminLoginAttemptTracker.MaxFailures; i++)
            await Record.ExceptionAsync(() => _service.LoginAsync(Login(code: "000000")));

        _clock.Advance(InMemoryLoginAttemptTracker.Window + TimeSpan.FromMinutes(1));

        (await Record.ExceptionAsync(() => _service.LoginAsync(Login()))).Should().BeOfType<TooManyRequestsException>();
    }

    [Fact]
    public async Task Repeated_failures_lock_the_account_even_against_the_right_credentials()
    {
        for (var i = 0; i < AdminLoginAttemptTracker.MaxFailures; i++)
            await Record.ExceptionAsync(() => _service.LoginAsync(Login(code: "000000")));

        var locked = await Record.ExceptionAsync(() => _service.LoginAsync(Login()));

        locked.Should().BeOfType<TooManyRequestsException>();
    }

    [Fact]
    public async Task The_lock_lifts_once_the_window_has_passed()
    {
        for (var i = 0; i < AdminLoginAttemptTracker.MaxFailures; i++)
            await Record.ExceptionAsync(() => _service.LoginAsync(Login(code: "000000")));

        _clock.Advance(AdminLoginAttemptTracker.Window + TimeSpan.FromMinutes(1));

        (await _service.LoginAsync(Login())).Token.Should().Be("admin-session-token");
    }

    [Fact]
    public async Task Nobody_can_sign_in_while_admin_access_is_not_configured()
    {
        _options.CurrentValue = new AdminOptions { Emails = [Email], TotpSecret = "" };

        var failure = await Record.ExceptionAsync(() => _service.LoginAsync(Login()));

        failure.Should().BeOfType<DomainException>();
        failure!.Message.Should().Be("Invalid email or code.");
    }

    [Fact]
    public async Task Removing_an_email_from_the_list_stops_that_person_signing_in()
    {
        _options.CurrentValue = new AdminOptions { Emails = ["someone-else@example.com"], TotpSecret = _secret };

        var failure = await Record.ExceptionAsync(() => _service.LoginAsync(Login()));

        failure.Should().BeOfType<DomainException>();
    }

    public void Dispose() => _fixture.Dispose();
}
