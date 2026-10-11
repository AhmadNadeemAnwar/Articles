using FluentValidation;
using Articles.Application.Admin.Dtos;

namespace Articles.Application.Admin.Validators;

public sealed class AdminLoginRequestValidator : AbstractValidator<AdminLoginRequest>
{
    public AdminLoginRequestValidator()
    {
        RuleFor(x => x.Email).NotEmpty().EmailAddress().MaximumLength(254);
        RuleFor(x => x.Code)
            .NotEmpty().WithMessage("Enter the 6-digit code from your authenticator app.")
            .Matches("^[0-9]{6}$").WithMessage("The code is 6 digits.");
    }
}

public sealed class RenameTagRequestValidator : AbstractValidator<RenameTagRequest>
{
    public RenameTagRequestValidator() => RuleFor(x => x.Name).NotEmpty().MaximumLength(40);
}

public sealed class MergeTagRequestValidator : AbstractValidator<MergeTagRequest>
{
    public MergeTagRequestValidator() => RuleFor(x => x.TargetTagId).NotEmpty();
}
