using FluentAssertions;
using Articles.Application.Auth;
using Articles.Application.Auth.Dtos;
using Articles.Application.Common;
using Articles.Domain.Entities;
using Articles.Domain.Exceptions;
using Articles.Infrastructure.Security;
using Moq;
using Xunit;

namespace Articles.Tests.Auth;

public class AuthHardeningTests : IDisposable
{
    private readonly TestDatabase _fixture = new();
    private readonly FakePwnedPasswordChecker _pwned = new();
    private readonly FakeTurnstileVerifier _turnstile = new();
    private readonly AuthService _service;

    public AuthHardeningTests()
    {
        var tokens = new Mock<ITokenService>();
        tokens.Setup(t => t.Create(It.IsAny<User>()))
            .Returns(new AccessToken("test-token", DateTimeOffset.UtcNow.AddHours(1)));

        _service = new AuthService(_fixture.Users, new FakePasswordHasher(), tokens.Object, _fixture.Db,
            _pwned, _turnstile, new InMemoryLoginAttemptTracker());
    }

    private static RegisterRequest Registration(string handle = "fresh-writer", string? token = null) =>
        new("fresh@example.com", handle, "Fresh Writer", "A-long-unguessable-phrase", token);

    [Theory]
    [InlineData("admin")]
    [InlineData("Admin")]
    [InlineData("  support  ")]
    [InlineData("articles")]
    [InlineData("me")]
    [InlineData("write")]
    public async Task Reserved_handles_cannot_be_registered(string handle)
    {
        var act = () => _service.RegisterAsync(Registration(handle));

        await act.Should().ThrowAsync<DomainException>().WithMessage("*reserved*");
    }

    [Fact]
    public async Task An_ordinary_handle_is_still_accepted() =>
        (await _service.RegisterAsync(Registration())).User.Handle.Should().Be("fresh-writer");

    [Fact]
    public async Task A_password_found_in_a_breach_is_rejected_and_no_account_is_created()
    {
        _pwned.Breached.Add("A-long-unguessable-phrase");

        var act = () => _service.RegisterAsync(Registration());

        await act.Should().ThrowAsync<DomainException>().WithMessage("*data breach*");
        (await _fixture.Users.EmailExistsAsync("fresh@example.com")).Should().BeFalse();
    }

    [Fact]
    public async Task Turnstile_is_skipped_when_it_is_not_configured()
    {
        _turnstile.IsEnabled = false;
        _turnstile.Passes = false;

        await _service.RegisterAsync(Registration());

        _turnstile.LastToken.Should().BeNull("an unconfigured verifier must not be consulted");
    }

    [Fact]
    public async Task A_failed_turnstile_check_blocks_sign_up()
    {
        _turnstile.IsEnabled = true;
        _turnstile.Passes = false;

        var act = () => _service.RegisterAsync(Registration(token: "bad-token"));

        await act.Should().ThrowAsync<DomainException>().WithMessage("*verification*");
        (await _fixture.Users.EmailExistsAsync("fresh@example.com")).Should().BeFalse();
    }

    [Fact]
    public async Task A_passing_turnstile_check_lets_sign_up_through_and_receives_the_token()
    {
        _turnstile.IsEnabled = true;
        _turnstile.Passes = true;

        await _service.RegisterAsync(Registration(token: "good-token"));

        _turnstile.LastToken.Should().Be("good-token");
    }

    [Fact]
    public async Task Ten_failed_sign_ins_lock_the_account_even_for_the_right_password()
    {
        await _service.RegisterAsync(Registration());

        for (var i = 0; i < InMemoryLoginAttemptTracker.MaxFailures; i++)
        {
            var wrong = () => _service.LoginAsync(new LoginRequest("fresh@example.com", "wrong"));
            await wrong.Should().ThrowAsync<DomainException>();
        }

        var locked = () => _service.LoginAsync(new LoginRequest("fresh@example.com", "A-long-unguessable-phrase"));

        await locked.Should().ThrowAsync<TooManyRequestsException>();
    }

    [Fact]
    public async Task Locking_one_account_does_not_affect_another()
    {
        await _service.RegisterAsync(Registration());
        await _service.RegisterAsync(new RegisterRequest("other@example.com", "other-writer", "Other", "Another-long-phrase-here"));

        for (var i = 0; i < InMemoryLoginAttemptTracker.MaxFailures; i++)
            await Record.ExceptionAsync(() => _service.LoginAsync(new LoginRequest("fresh@example.com", "wrong")));

        var response = await _service.LoginAsync(new LoginRequest("other@example.com", "Another-long-phrase-here"));

        response.User.Handle.Should().Be("other-writer");
    }

    [Fact]
    public async Task A_successful_sign_in_resets_the_failure_count()
    {
        await _service.RegisterAsync(Registration());

        for (var round = 0; round < 3; round++)
        {
            for (var i = 0; i < InMemoryLoginAttemptTracker.MaxFailures - 1; i++)
                await Record.ExceptionAsync(() => _service.LoginAsync(new LoginRequest("fresh@example.com", "wrong")));

            // Nine failures each round would lock the account by round two if the counter were never cleared.
            var ok = await _service.LoginAsync(new LoginRequest("fresh@example.com", "A-long-unguessable-phrase"));
            ok.Token.Should().Be("test-token");
        }
    }

    [Fact]
    public async Task Lockout_cannot_be_used_to_tell_real_emails_from_made_up_ones()
    {
        await _service.RegisterAsync(Registration());

        async Task<Type?> LockOut(string email)
        {
            for (var i = 0; i < InMemoryLoginAttemptTracker.MaxFailures; i++)
                await Record.ExceptionAsync(() => _service.LoginAsync(new LoginRequest(email, "wrong")));

            return (await Record.ExceptionAsync(() => _service.LoginAsync(new LoginRequest(email, "wrong"))))?.GetType();
        }

        (await LockOut("fresh@example.com")).Should().Be(typeof(TooManyRequestsException));
        (await LockOut("ghost@example.com")).Should().Be(typeof(TooManyRequestsException));
    }

    public void Dispose() => _fixture.Dispose();
}
