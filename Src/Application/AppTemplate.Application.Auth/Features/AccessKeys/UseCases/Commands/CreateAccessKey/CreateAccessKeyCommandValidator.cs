using AppTemplate.Application.Auth.Features.AccessKeys.Model.Policies;
using AppTemplate.Application.Auth.Features.Accounts.Model.Policies;
using FluentValidation;

namespace AppTemplate.Application.Auth.Features.AccessKeys.UseCases.Commands.CreateAccessKey;

public sealed class CreateAccessKeyCommandValidator : AbstractValidator<CreateAccessKeyCommand>
{
    public CreateAccessKeyCommandValidator()
    {
        RuleFor(x => x.Name)
            .NotEmpty().WithMessage("Name is required.")
            .MaximumLength(AccessKeyPolicy.NameMaxLength)
                .WithMessage($"Name must not exceed {AccessKeyPolicy.NameMaxLength} characters.");

        RuleFor(x => x.Scopes)
            .Cascade(CascadeMode.Stop)
            .NotEmpty().WithMessage("At least one scope is required.")
            .Must(scopes => scopes.Distinct(StringComparer.Ordinal).Count() == scopes.Count)
                .WithMessage("A scope may be named once.");

        RuleForEach(x => x.Scopes)
            .Must(scope => AccessKeyPolicy.Scopes.Contains(scope, StringComparer.Ordinal))
            .WithMessage($"A scope is one of: {string.Join(", ", AccessKeyPolicy.Scopes)}.");

        RuleFor(x => x.CurrentPassword)
            .MaximumLength(PasswordInputPolicy.MaximumLength)
            .When(x => x.CurrentPassword is not null);
    }
}
