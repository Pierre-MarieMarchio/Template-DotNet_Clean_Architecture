using AppTemplate.Application.Auth.Features.TwoFactor.Model.Errors;
using AppTemplate.Application.Auth.Features.TwoFactor.Ports.TwoFactor;
using AppTemplate.Application.Core.Common.Ports;
using AppTemplate.Application.Core.Common.Results;

namespace AppTemplate.Application.Auth.Features.TwoFactor.UseCases.Commands.SetUpTwoFactor;

/// <summary>Begins two-factor enrollment: hands back a shared key and an <c>otpauth://</c> URI to
/// scan, but arms nothing yet — <see cref="Commands.ConfirmTwoFactorSetup"/> does that. No
/// <c>CredentialInvalidationPolicy</c> here, on purpose: provisioning a secret rotates the security
/// stamp through Identity's own <c>ResetAuthenticatorKeyAsync</c>, and revoking every refresh token
/// on top of that would sign every device out of a step that has changed nothing yet.</summary>
public sealed class SetUpTwoFactorUseCase(
    ITwoFactorService twoFactor,
    ICurrentUser currentUser) : ISetUpTwoFactorUseCase
{
    /// <inheritdoc />
    public async Task<Result<SetUpTwoFactorOutcome>> ExecuteAsync(CancellationToken cancellationToken = default)
    {
        var userId = currentUser.RequireUserId();

        if (userId.IsFailure)
        {
            return userId.To<SetUpTwoFactorOutcome>();
        }

        var started = await twoFactor.BeginSetupAsync(userId.Value.Value, cancellationToken);

        if (started.Status is not TwoFactorSetupStatus.Started)
        {
            return Result.Failure<SetUpTwoFactorOutcome>(ToError(started.Status));
        }

        return Result.Success(new SetUpTwoFactorOutcome(started.SharedKey!, started.AuthenticatorUri!));
    }

    private static Error ToError(TwoFactorSetupStatus status) => status switch
    {
        TwoFactorSetupStatus.AlreadyEnabled => TwoFactorErrors.TwoFactorAlreadyEnabled,

        // A token that outlived its account, answered as GetCurrentUserUseCase answers it.
        TwoFactorSetupStatus.NoSuchAccount => CommonErrors.NotAuthenticated,
        _ => TwoFactorErrors.TwoFactorSetupRejected,
    };
}
