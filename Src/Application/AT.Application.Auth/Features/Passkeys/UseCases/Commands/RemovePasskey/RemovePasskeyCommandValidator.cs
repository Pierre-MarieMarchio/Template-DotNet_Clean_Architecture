using FluentValidation;

namespace AppTemplate.Application.Auth.Features.Passkeys.UseCases.Commands.RemovePasskey;

public sealed class RemovePasskeyCommandValidator : AbstractValidator<RemovePasskeyCommand>
{
    public RemovePasskeyCommandValidator() => RuleFor(x => x.CredentialId).NotEmpty();
}
