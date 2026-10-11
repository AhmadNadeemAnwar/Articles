using FluentValidation;
using Articles.Application.Comments.Dtos;
using Articles.Domain.Entities;

namespace Articles.Application.Comments.Validators;

public sealed class CreateCommentRequestValidator : AbstractValidator<CreateCommentRequest>
{
    public CreateCommentRequestValidator()
    {
        RuleFor(x => x.Body)
            .NotEmpty().WithMessage("Write something before posting.")
            .MaximumLength(Comment.MaxBodyLength);
    }
}

public sealed class UpdateCommentRequestValidator : AbstractValidator<UpdateCommentRequest>
{
    public UpdateCommentRequestValidator()
    {
        RuleFor(x => x.Body)
            .NotEmpty().WithMessage("Write something before posting.")
            .MaximumLength(Comment.MaxBodyLength);
    }
}
