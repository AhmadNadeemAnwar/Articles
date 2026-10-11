using FluentValidation;
using Articles.Application.Users.Dtos;
using Articles.Domain.Common;

namespace Articles.Application.Users.Validators;

public sealed class UpdateProfileRequestValidator : AbstractValidator<UpdateProfileRequest>
{
    public UpdateProfileRequestValidator()
    {
        RuleFor(x => x.DisplayName)
            .NotEmpty().WithMessage("Display name is required.")
            .MaximumLength(60);

        RuleFor(x => x.Bio).MaximumLength(300);
        RuleFor(x => x.AvatarUrl).MaximumLength(2048)
            .Must(UrlRules.IsImageReference).When(x => !string.IsNullOrWhiteSpace(x.AvatarUrl))
            .WithMessage("Photo must be an uploaded picture or a link starting with https://.");

        RuleFor(x => x.WebsiteUrl).MaximumLength(2048)
            .Must(UrlRules.IsHttpOrHttps).When(x => !string.IsNullOrWhiteSpace(x.WebsiteUrl))
            .WithMessage("Website must be a link starting with http:// or https://.");
    }
}
