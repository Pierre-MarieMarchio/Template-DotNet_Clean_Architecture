using AppTemplate.Application.Auth.Features.Auth.Errors;
using AppTemplate.Application.Auth.Features.Auth.Ports.Accounts;
using AppTemplate.Application.Auth.Features.Auth.Ports.Passkeys;
using AppTemplate.Application.Auth.Features.Auth.Ports.ReAuthentication;
using AppTemplate.Application.Auth.Features.Auth.Ports.SecurityEventLog;
using AppTemplate.Application.Auth.Features.Auth.UseCases.Commands.DeleteMyAccount;
using AppTemplate.Application.Core.Common.Ports;
using AppTemplate.Application.Core.Common.Results;
using AppTemplate.Application.Core.Common.Validation;
using FluentValidation;

namespace AppTemplate.Application.Auth.Features.Auth.UseCases.Commands.CompletePasskeyRegistration;

/// <summary>Completes a passkey registration and stores the credential, once the holder proves the
/// session is the account's own and not merely a live one — the reason
/// <c>ConfirmTwoFactorSetupUseCase</c> gives, with higher stakes: a passkey is a whole way in, and
/// a stolen session that could register one would hand its thief sign-in for ever after. The proof
/// is the shape account deletion takes — the password, or for a provider-linked account an access
/// token issued within <see cref="DeleteMyAccountUseCase.RecentAuthenticationWindow"/>. Registering
/// a credential ends no session and rotates no stamp; removing one is the same.</summary>
public sealed class CompletePasskeyRegistrationUseCase(
    IPasskeyService passkeys,
    IAccountsService accounts,
    IReAuthenticationService reAuthentication,
    ICurrentAccessToken currentAccessToken,
    IDateTimeProvider dateTimeProvider,
    ISecurityEventLog securityEventLog,
    ICurrentUser currentUser,
    IValidator<CompletePasskeyRegistrationCommand> validator) : ICompletePasskeyRegistrationUseCase
{
    /// <inheritdoc />
    public async Task<Result<PasskeyCredential>> ExecuteAsync(
        CompletePasskeyRegistrationCommand request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var validation = await validator.EnsureValidAsync(request, cancellationToken);

        if (validation.IsFailure)
        {
            return validation.To<PasskeyCredential>();
        }

        var callerId = currentUser.RequireUserId();

        if (callerId.IsFailure)
        {
            return callerId.To<PasskeyCredential>();
        }

        var userId = callerId.Value.Value;
        var profile = await accounts.FindProfileAsync(userId, cancellationToken);

        // A token that outlived its account, answered as GetCurrentUserUseCase answers it.
        if (profile is null)
        {
            return Result.Failure<PasskeyCredential>(CommonErrors.NotAuthenticated);
        }

        // The proof before the ceremony: a refused proof leaves the state the client holds usable,
        // so a retry after presenting the right evidence needs no second options call.
        if (profile.HasPassword)
        {
            if (request.CurrentPassword is null)
            {
                return Result.Failure<PasskeyCredential>(AuthErrors.MissingCurrentPassword);
            }

            if (!await reAuthentication.VerifyPasswordAsync(userId, request.CurrentPassword, cancellationToken))
            {
                return Result.Failure<PasskeyCredential>(AuthErrors.IncorrectCurrentPassword);
            }
        }
        else if (currentAccessToken.IssuedAt is not { } issuedAt
            || dateTimeProvider.UtcNow - issuedAt > DeleteMyAccountUseCase.RecentAuthenticationWindow)
        {
            // The fallback proof: the token itself, recently issued. A missing claim is a refusal,
            // never a pass.
            return Result.Failure<PasskeyCredential>(AuthErrors.ReAuthenticationRequired);
        }

        var registration = await passkeys.CompleteRegistrationAsync(
            userId,
            request.Credential,
            request.RegistrationState,
            request.Name,
            cancellationToken);

        return registration.Status switch
        {
            PasskeyRegistrationStatus.Registered => Registered(userId, registration.Credential!),
            PasskeyRegistrationStatus.NoSuchAccount => Result.Failure<PasskeyCredential>(CommonErrors.NotAuthenticated),
            PasskeyRegistrationStatus.StoreRejected => Result.Failure<PasskeyCredential>(AuthErrors.PasskeyRegistrationRejected),
            _ => Result.Failure<PasskeyCredential>(AuthErrors.InvalidPasskeyRegistration),
        };
    }

    private Result<PasskeyCredential> Registered(Guid userId, PasskeyCredential credential)
    {
        securityEventLog.Record(SecurityEvent.PasskeyRegistered(userId));

        return Result.Success(credential);
    }
}
