using FluentValidation;
using Articles.Application.Auth.Dtos;

namespace Articles.Application.Auth.Validators;

public sealed class RegisterRequestValidator : AbstractValidator<RegisterRequest>
{
    public RegisterRequestValidator()
    {
        RuleFor(x => x.Email)
            .NotEmpty().WithMessage("Email is required.")
            .EmailAddress().WithMessage("Enter a valid email address.")
            .MaximumLength(254);

        RuleFor(x => x.Handle)
            .NotEmpty().WithMessage("Handle is required.")
            .MinimumLength(3).WithMessage("Handle must be at least 3 characters.")
            .MaximumLength(30)
            .Matches("^[a-zA-Z0-9_-]+$")
            .WithMessage("Handle can only contain letters, numbers, hyphens and underscores.");

        RuleFor(x => x.DisplayName)
            .NotEmpty().WithMessage("Display name is required.")
            .MaximumLength(60);

        RuleFor(x => x.Password)
            .NotEmpty().WithMessage("Password is required.")
            .MaximumLength(PasswordPolicy.MaxLength);

        RuleFor(x => x)
            .Custom((request, context) =>
            {
                if (string.IsNullOrEmpty(request.Password)) return;

                var problem = PasswordPolicy.Check(request.Password, request.Email ?? "", request.Handle ?? "", request.DisplayName ?? "");
                if (problem is not null) context.AddFailure(nameof(RegisterRequest.Password), problem);
            });

        RuleFor(x => x.TurnstileToken).MaximumLength(2048);
    }
}

public sealed class LoginRequestValidator : AbstractValidator<LoginRequest>
{
    public LoginRequestValidator()
    {
        RuleFor(x => x.Email).NotEmpty().EmailAddress();
        RuleFor(x => x.Password).NotEmpty();
    }
}
