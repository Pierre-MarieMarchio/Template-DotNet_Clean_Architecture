using AppTemplate.Application.Auth.Features.Accounts.Ports.Accounts;
using AppTemplate.Application.Auth.Features.Passkeys.Ports.Passkeys;
using AppTemplate.Application.Core.Common.Ports;
using AppTemplate.Application.Core.Common.Results;
using Mediator;

namespace AppTemplate.Application.Auth.Features.Passkeys.UseCases.Commands.BeginPasskeyRegistration;

public sealed record BeginPasskeyRegistrationCommand : ICommand<Result<PasskeyRegistrationChallenge>>;

public sealed class BeginPasskeyRegistrationCommandHandler(
    IAccountsService accounts,
    IPasskeyService passkeys,
    ICurrentUser currentUser) : ICommandHandler<BeginPasskeyRegistrationCommand, Result<PasskeyRegistrationChallenge>>
{
    public async ValueTask<Result<PasskeyRegistrationChallenge>> Handle(
        BeginPasskeyRegistrationCommand command,
        CancellationToken cancellationToken)
    {
        var callerId = currentUser.RequireUserId();

        if (callerId.IsFailure)
        {
            return callerId.To<PasskeyRegistrationChallenge>();
        }

        if (await accounts.FindProfileAsync(callerId.Value.Value, cancellationToken) is null)
        {
            return Result.Failure<PasskeyRegistrationChallenge>(CommonErrors.NotAuthenticated);
        }

        return Result.Success(
            await passkeys.BeginRegistrationAsync(callerId.Value.Value, cancellationToken));
    }
}
