using FluentAssertions;
using Articles.Application.Auth;
using Articles.Application.Auth.Dtos;
using Articles.Application.Auth.Validators;
using Xunit;

namespace Articles.Tests.Auth;

public class PasswordPolicyTests
{
    private static string? Check(string password) =>
        PasswordPolicy.Check(password, "ada.lovelace@example.com", "ada-l", "Ada Lovelace");

    [Fact]
    public void A_long_unpredictable_passphrase_is_accepted() =>
        Check("correct horse battery staple").Should().BeNull();

    [Theory]
    [InlineData("short1!")]
    [InlineData("123456789")]
    public void Passwords_under_ten_characters_are_rejected(string password) =>
        Check(password).Should().Contain("at least 10");

    [Theory]
    [InlineData("password123")]
    [InlineData("PASSWORD1234")]
    [InlineData("1234567890")]
    [InlineData("qwertyuiop")]
    public void Well_known_passwords_are_rejected_regardless_of_case(string password) =>
        Check(password).Should().Contain("too common");

    [Theory]
    [InlineData("ada.lovelace@example.com-123")]
    [InlineData("MyNameIsAda.Lovelace")]
    [InlineData("xx-ada-l-xx-secret")]
    [InlineData("Ada Lovelace rocks 42")]
    public void Passwords_built_from_the_users_own_details_are_rejected(string password) =>
        Check(password).Should().Contain("name, handle or email");

    [Fact]
    public void A_password_of_one_repeated_character_is_rejected() =>
        Check("aaaaaaaaaaaaaaaa").Should().Contain("variety");

    [Fact]
    public void Overlong_passwords_are_rejected() =>
        Check(new string('x', 129) + "yz12").Should().Contain("exceed");

    [Fact]
    public void The_registration_validator_reports_a_weak_password_against_the_password_field()
    {
        var result = new RegisterRequestValidator().Validate(
            new RegisterRequest("ada@example.com", "ada-l", "Ada", "password123"));

        result.IsValid.Should().BeFalse();
        result.Errors.Should().ContainSingle(e => e.PropertyName == "Password" && e.ErrorMessage.Contains("too common"));
    }

    [Fact]
    public void The_registration_validator_accepts_a_strong_password()
    {
        var result = new RegisterRequestValidator().Validate(
            new RegisterRequest("ada@example.com", "ada-l", "Ada", "correct horse battery staple"));

        result.IsValid.Should().BeTrue();
    }
}
