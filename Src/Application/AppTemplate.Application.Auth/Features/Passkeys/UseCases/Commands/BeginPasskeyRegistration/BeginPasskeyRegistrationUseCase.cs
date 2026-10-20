using AppTemplate.Application.Auth.Features.Accounts.Ports.Accounts;
using AppTemplate.Application.Auth.Features.Passkeys.Ports.Passkeys;
using AppTemplate.Application.Core.Common.Ports;
using AppTemplate.Application.Core.Common.Results;

namespace AppTemplate.Application.Auth.Features.Passkeys.UseCases.Commands.BeginPasskeyRegistration;

public sealed class BeginPasskeyRegistrationUseCase(
    IAccountsService accounts,
    IPasskeyService passkeys,
    ICurrentUser currentUser) : IBeginPasskeyRegistrationUseCase
{
    public async Task<Result<PasskeyRegistrationChallenge>> ExecuteAsync(
        CancellationToken cancellationToken = default)
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
