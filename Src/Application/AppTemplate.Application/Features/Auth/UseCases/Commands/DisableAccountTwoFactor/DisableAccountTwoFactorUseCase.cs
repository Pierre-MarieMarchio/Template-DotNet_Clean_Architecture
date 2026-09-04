using AppTemplate.Application.Common.Abstractions;
using AppTemplate.Application.Common.Results;
using AppTemplate.Application.Common.Validation;
using AppTemplate.Application.Features.Auth.Errors;
using AppTemplate.Application.Features.Auth.Policies;
using AppTemplate.Application.Features.Auth.Ports.RefreshTokenGrants;
using AppTemplate.Application.Features.Auth.Ports.SecurityEventLog;
using AppTemplate.Application.Features.Auth.Ports.TwoFactorAdministration;
using FluentValidation;

namespace AppTemplate.Application.Features.Auth.UseCases.Commands.DisableAccountTwoFactor;

/// <summary>
/// The self-guard here is not <see cref="SelfAdministrationPolicy"/>'s usual reason. Disabling two
/// factor on the caller's own account is not the self-inflicted, nobody-left-to-undo-it lockout that
/// <see cref="AuthErrors.CannotLockOwnAccount"/> and <see cref="AuthErrors.CannotDeleteOwnAccount"/>
/// refuse — sign-in still works afterward, just with one fewer step. It is refused for the reason
/// this whole capability exists to close: an administrator's own session is a session like any
/// other, and letting it reach this capability against its own account would let a stolen one strip
/// its second factor without ever producing the password <c>ITwoFactorEnrollmentService.DisableAsync</c>
/// demands of everybody else. See <see cref="AuthErrors.CannotDisableOwnTwoFactor"/>.
/// </summary>
public sealed class DisableAccountTwoFactorUseCase(
    ITwoFactorAdministrationService administration,
    IRefreshTokenGrantsService refreshTokens,
    ISecurityEventLog securityEventLog,
    ICurrentUser currentUser,
    IValidator<DisableAccountTwoFactorCommand> validator) : IDisableAccountTwoFactorUseCase
{
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
            callerId.Value,
            request.UserId,
            AuthErrors.CannotDisableOwnTwoFactor);

        if (guard.IsFailure)
        {
            return guard;
        }

        var outcome = await administration.DisableAsync(request.UserId, cancellationToken);

        if (outcome is not TwoFactorAdministrativeDisableStatus.Disabled)
        {
            return Result.Failure(ToError(outcome));
        }

        await CredentialInvalidationPolicy.InvalidateAsync(refreshTokens, securityEventLog, request.UserId, cancellationToken);
        securityEventLog.Record(SecurityEvent.TwoFactorDisabledByAdministrator(request.UserId));

        return Result.Success();
    }

    private static Error ToError(TwoFactorAdministrativeDisableStatus outcome) => outcome switch
    {
        TwoFactorAdministrativeDisableStatus.NoSuchAccount => AuthErrors.NoSuchAccount,
        _ => AuthErrors.TwoFactorAdministrativeDisableRejected,
    };
}
