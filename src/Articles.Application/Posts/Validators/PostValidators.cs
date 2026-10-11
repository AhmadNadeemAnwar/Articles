using FluentValidation;
using Articles.Application.Posts.Dtos;
using Articles.Domain.Common;
using Articles.Domain.Entities;

namespace Articles.Application.Posts.Validators;

public sealed class CreatePostRequestValidator : AbstractValidator<CreatePostRequest>
{
    public CreatePostRequestValidator()
    {
        RuleFor(x => x.Title)
            .NotEmpty().WithMessage("Give your post a title.")
            .MaximumLength(Post.MaxTitleLength);

        RuleFor(x => x.Subtitle).MaximumLength(Post.MaxSubtitleLength);
        RuleFor(x => x.ContentJson).NotEmpty().WithMessage("Post content is required.");
        RuleFor(x => x.CoverImageUrl).MaximumLength(2048)
            .Must(UrlRules.IsImageReference).When(x => !string.IsNullOrWhiteSpace(x.CoverImageUrl))
            .WithMessage("Cover image must be an uploaded picture or a link starting with https://.");

        RuleFor(x => x.Tags)
            .Must(tags => tags is null || tags.Count <= 5)
            .WithMessage("A post can have at most 5 tags.");
    }
}

public sealed class UpdatePostRequestValidator : AbstractValidator<UpdatePostRequest>
{
    public UpdatePostRequestValidator()
    {
        RuleFor(x => x.Title)
            .NotEmpty().WithMessage("Give your post a title.")
            .MaximumLength(Post.MaxTitleLength);

        RuleFor(x => x.Subtitle).MaximumLength(Post.MaxSubtitleLength);
        RuleFor(x => x.ContentJson).NotEmpty().WithMessage("Post content is required.");
        RuleFor(x => x.CoverImageUrl).MaximumLength(2048)
            .Must(UrlRules.IsImageReference).When(x => !string.IsNullOrWhiteSpace(x.CoverImageUrl))
            .WithMessage("Cover image must be an uploaded picture or a link starting with https://.");

        RuleFor(x => x.Tags)
            .Must(tags => tags is null || tags.Count <= 5)
            .WithMessage("A post can have at most 5 tags.");
    }
}
