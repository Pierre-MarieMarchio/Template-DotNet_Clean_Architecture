using AppTemplate.Application.Auth.Features.Passkeys.Ports.Passkeys;
using AppTemplate.Application.Core.Common.Results;
using Mediator;

namespace AppTemplate.Application.Auth.Features.Passkeys.UseCases.Commands.BeginPasskeySignIn;

public sealed record BeginPasskeySignInCommand : ICommand<Result<PasskeySignInChallenge>>;

public sealed class BeginPasskeySignInCommandHandler(IPasskeyService passkeys) : ICommandHandler<BeginPasskeySignInCommand, Result<PasskeySignInChallenge>>
{
    public async ValueTask<Result<PasskeySignInChallenge>> Handle(BeginPasskeySignInCommand command, CancellationToken cancellationToken) =>
        Result.Success(await passkeys.BeginSignInAsync(cancellationToken));
}
