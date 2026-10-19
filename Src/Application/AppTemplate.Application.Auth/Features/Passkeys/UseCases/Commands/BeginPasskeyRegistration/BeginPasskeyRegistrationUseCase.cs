using AppTemplate.Application.Auth.Features.Accounts.Ports.Accounts;
using AppTemplate.Application.Auth.Features.Passkeys.Ports.Passkeys;
using AppTemplate.Application.Core.Common.Ports;
using AppTemplate.Application.Core.Common.Results;

namespace AppTemplate.Application.Auth.Features.Passkeys.UseCases.Commands.BeginPasskeyRegistration;

/// <summary>
/// Starts registering a passkey on the caller's own account: the creation options a client feeds
/// to <c>navigator.credentials.create()</c>, and the state that joins the two calls. Arming
/// nothing — nothing is stored until <c>CompletePasskeyRegistration</c> has both the credential and
/// the re-authentication its step demands.
/// </summary>
public sealed class BeginPasskeyRegistrationUseCase(
    IAccountsService accounts,
    IPasskeyService passkeys,
    ICurrentUser currentUser) : IBeginPasskeyRegistrationUseCase
{
    /// <inheritdoc />
    public async Task<Result<PasskeyRegistrationChallenge>> ExecuteAsync(
        CancellationToken cancellationToken = default)
    {
        var callerId = currentUser.RequireUserId();

        if (callerId.IsFailure)
        {
            return callerId.To<PasskeyRegistrationChallenge>();
        }

        // A token that outlived its account, answered as GetCurrentUserUseCase answers it. Checked
        // here so the port's own missing-account refusal never has to fire on this step.
        if (await accounts.FindProfileAsync(callerId.Value.Value, cancellationToken) is null)
        {
            return Result.Failure<PasskeyRegistrationChallenge>(CommonErrors.NotAuthenticated);
        }

        return Result.Success(
            await passkeys.BeginRegistrationAsync(callerId.Value.Value, cancellationToken));
    }
}
