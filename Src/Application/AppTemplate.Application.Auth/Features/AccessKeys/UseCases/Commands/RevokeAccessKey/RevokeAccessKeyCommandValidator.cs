using FluentValidation;

namespace AppTemplate.Application.Auth.Features.AccessKeys.UseCases.Commands.RevokeAccessKey;

public sealed class RevokeAccessKeyCommandValidator : AbstractValidator<RevokeAccessKeyCommand>
{
    public RevokeAccessKeyCommandValidator() => RuleFor(x => x.AccessKeyId).NotEmpty();
}
