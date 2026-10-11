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

public class AuthServiceTests : IDisposable
{
    private readonly TestDatabase _fixture = new();
    private readonly AuthService _service;
    private readonly FakePwnedPasswordChecker _pwned = new();
    private readonly FakeTurnstileVerifier _turnstile = new();
    private readonly InMemoryLoginAttemptTracker _attempts = new();

    public AuthServiceTests()
    {
        var tokens = new Mock<ITokenService>();
        tokens.Setup(t => t.Create(It.IsAny<User>()))
            .Returns(new AccessToken("test-token", DateTimeOffset.UtcNow.AddHours(1)));

        _service = new AuthService(_fixture.Users, new FakePasswordHasher(), tokens.Object, _fixture.Db, _pwned, _turnstile, _attempts);
    }

    private static RegisterRequest Registration(string email = "New@Example.com", string handle = "New Writer") =>
        new(email, handle, "New Writer", "Password123!");

    [Fact]
    public async Task Register_normalises_the_email_and_slugifies_the_handle()
    {
        var response = await _service.RegisterAsync(Registration());

        response.User.Email.Should().Be("new@example.com");
        response.User.Handle.Should().Be("new-writer");
        response.Token.Should().Be("test-token");
    }

    [Fact]
    public async Task Register_never_stores_the_password_in_plain_text()
    {
        await _service.RegisterAsync(Registration());

        var stored = await _fixture.Users.GetByEmailAsync("new@example.com");
        stored!.PasswordHash.Should().NotBe("Password123!");
        stored.PasswordHash.Should().Be("hashed:Password123!");
    }

    [Fact]
    public async Task A_duplicate_email_is_rejected()
    {
        await _service.RegisterAsync(Registration());

        var act = () => _service.RegisterAsync(Registration(handle: "Another Handle"));

        await act.Should().ThrowAsync<ConflictException>().WithMessage("*email already exists*");
    }

    [Fact]
    public async Task A_duplicate_handle_is_rejected()
    {
        await _service.RegisterAsync(Registration());

        var act = () => _service.RegisterAsync(Registration(email: "other@example.com"));

        await act.Should().ThrowAsync<ConflictException>().WithMessage("*is taken*");
    }

    [Fact]
    public async Task Login_succeeds_with_the_correct_password_regardless_of_email_casing()
    {
        await _service.RegisterAsync(Registration());

        var response = await _service.LoginAsync(new LoginRequest("NEW@example.com", "Password123!"));

        response.User.Handle.Should().Be("new-writer");
    }

    [Fact]
    public async Task A_wrong_password_and_an_unknown_email_fail_identically()
    {
        await _service.RegisterAsync(Registration());

        var wrongPassword = await Record.ExceptionAsync(
            () => _service.LoginAsync(new LoginRequest("new@example.com", "not-the-password")));

        var unknownEmail = await Record.ExceptionAsync(
            () => _service.LoginAsync(new LoginRequest("nobody@example.com", "Password123!")));

        // Identical messages keep the endpoint from confirming which emails are registered.
        wrongPassword.Should().BeOfType<DomainException>();
        unknownEmail.Should().BeOfType<DomainException>();
        wrongPassword!.Message.Should().Be(unknownEmail!.Message);
    }

    public void Dispose() => _fixture.Dispose();
}
