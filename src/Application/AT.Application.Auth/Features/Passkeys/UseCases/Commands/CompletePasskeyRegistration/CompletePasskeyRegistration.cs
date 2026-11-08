using AppTemplate.Application.Auth.Features.Accounts.Model.Errors;
using AppTemplate.Application.Auth.Features.Accounts.Ports.Accounts;
using AppTemplate.Application.Auth.Features.Accounts.Ports.ReAuthentication;
using AppTemplate.Application.Auth.Features.Accounts.UseCases.Commands.DeleteMyAccount;
using AppTemplate.Application.Auth.Features.Passkeys.Model.Errors;
using AppTemplate.Application.Auth.Features.Passkeys.Ports.Passkeys;
using AppTemplate.Application.Auth.Features.SecurityEvents.Ports.SecurityEventLog;
using AppTemplate.Application.Core.Common.Ports;
using AppTemplate.Application.Core.Common.Results;
using Mediator;

namespace AppTemplate.Application.Auth.Features.Passkeys.UseCases.Commands.CompletePasskeyRegistration;

public sealed record CompletePasskeyRegistrationCommand(
    string Credential,
    string RegistrationState,
    string? Name,
    string? CurrentPassword) : ICommand<Result<PasskeyCredential>>;

public sealed class CompletePasskeyRegistrationCommandHandler(
    IPasskeyService passkeys,
    IAccountsService accounts,
    IReAuthenticationService reAuthentication,
    ICurrentAccessToken currentAccessToken,
    TimeProvider timeProvider,
    ISecurityEventLog securityEventLog,
    ICurrentUser currentUser) : ICommandHandler<CompletePasskeyRegistrationCommand, Result<PasskeyCredential>>
{
    public async ValueTask<Result<PasskeyCredential>> Handle(
        CompletePasskeyRegistrationCommand request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var callerId = currentUser.RequireUserId();

        if (callerId.IsFailure)
        {
            return callerId.To<PasskeyCredential>();
        }

        var userId = callerId.Value.Value;
        var profile = await accounts.FindProfileAsync(userId, cancellationToken);

        if (profile is null)
        {
            return Result.Failure<PasskeyCredential>(CommonErrors.NotAuthenticated);
        }

        if (profile.HasPassword)
        {
            if (request.CurrentPassword is null)
            {
                return Result.Failure<PasskeyCredential>(AccountsErrors.MissingCurrentPassword);
            }

            if (!await reAuthentication.VerifyPasswordAsync(userId, request.CurrentPassword, cancellationToken))
            {
                return Result.Failure<PasskeyCredential>(AccountsErrors.IncorrectCurrentPassword);
            }
        }
        else if (currentAccessToken.IssuedAt is not { } issuedAt
            || timeProvider.GetUtcNow() - issuedAt > DeleteMyAccountCommandHandler.RecentAuthenticationWindow)
        {
            return Result.Failure<PasskeyCredential>(AccountsErrors.ReAuthenticationRequired);
        }

        var registration = await passkeys.CompleteRegistrationAsync(
            userId,
            request.Credential,
            request.RegistrationState,
            request.Name,
            cancellationToken);

        return registration.Status switch
        {
            PasskeyRegistrationStatus.Registered => await RegisteredAsync(userId, registration.Credential!),
            PasskeyRegistrationStatus.NoSuchAccount => Result.Failure<PasskeyCredential>(CommonErrors.NotAuthenticated),
            PasskeyRegistrationStatus.StoreRejected => Result.Failure<PasskeyCredential>(PasskeysErrors.PasskeyRegistrationRejected),
            _ => Result.Failure<PasskeyCredential>(PasskeysErrors.InvalidPasskeyRegistration),
        };
    }

    private async Task<Result<PasskeyCredential>> RegisteredAsync(Guid userId, PasskeyCredential credential)
    {
        await securityEventLog.RecordAsync(SecurityEvent.PasskeyRegistered(userId));

        return Result.Success(credential);
    }
}
