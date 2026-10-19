using AppTemplate.Application.Auth.Features.Accounts.Model.Errors;
using AppTemplate.Application.Auth.Features.Accounts.Model.Policies;
using AppTemplate.Application.Auth.Features.Accounts.Ports.AccountAdministration;
using AppTemplate.Application.Auth.Features.SecurityEvents.Ports.SecurityEventLog;
using AppTemplate.Application.Auth.Features.Sessions.Ports.SessionTokens;
using AppTemplate.Application.Auth.Features.TwoFactor.Model.Errors;
using AppTemplate.Application.Core.Common.Ports;
using AppTemplate.Application.Core.Common.Results;
using AppTemplate.Application.Core.Common.Validation;
using FluentValidation;

namespace AppTemplate.Application.Auth.Features.TwoFactor.UseCases.Commands.DisableAccountTwoFactor;

/// <summary>
/// The self-guard here is not <see cref="SelfAdministrationPolicy"/>'s usual reason. Disabling two
/// factor on the caller's own account is not the self-inflicted, nobody-left-to-undo-it lockout that
/// <see cref="AccountsErrors.CannotLockOwnAccount"/> and <see cref="AccountsErrors.CannotDeleteOwnAccount"/>
/// refuse — sign-in still works afterward, just with one fewer step. It is refused for the reason
/// this whole capability exists to close: an administrator's own session is a session like any
/// other, and letting it reach this capability against its own account would let a stolen one strip
/// its second factor without ever producing the password <c>ITwoFactorService.DisableAsync</c>
/// demands of everybody else. See <see cref="TwoFactorErrors.CannotDisableOwnTwoFactor"/>.
/// </summary>
public sealed class DisableAccountTwoFactorUseCase(
    IAccountAdministrationService administration,
    ISessionTokensService sessionTokens,
    ISecurityEventLog securityEventLog,
    ICurrentUser currentUser,
    IValidator<DisableAccountTwoFactorCommand> validator) : IDisableAccountTwoFactorUseCase
{
    /// <inheritdoc />
    public async Task<Result> ExecuteAsync(
        DisableAccountTwoFactorCommand request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var validation = await validator.EnsureValidAsync(request, cancellationToken);

        if (validation.IsFailure)
        {
            return validation;
        }

        var callerId = currentUser.RequireUserId();

        if (callerId.IsFailure)
        {
            return callerId;
        }

        var guard = SelfAdministrationPolicy.EnsureNotSelf(
            callerId.Value.Value,
            request.UserId,
            TwoFactorErrors.CannotDisableOwnTwoFactor);

        if (guard.IsFailure)
        {
            return guard;
        }

        var outcome = await administration.DisableTwoFactorAsync(request.UserId, cancellationToken);

        if (outcome.Status is not AccountAdministrationStatus.Applied)
        {
            return Result.Failure(ToError(outcome.Status));
        }

        await CredentialInvalidationPolicy.InvalidateAsync(sessionTokens, securityEventLog, request.UserId, cancellationToken);
        securityEventLog.Record(SecurityEvent.TwoFactorDisabledByAdministrator(request.UserId));

        return Result.Success();
    }

    private static Error ToError(AccountAdministrationStatus status) => status switch
    {
        AccountAdministrationStatus.NoSuchAccount => AccountsErrors.NoSuchAccount,
        _ => TwoFactorErrors.TwoFactorAdministrativeDisableRejected,
    };
}
