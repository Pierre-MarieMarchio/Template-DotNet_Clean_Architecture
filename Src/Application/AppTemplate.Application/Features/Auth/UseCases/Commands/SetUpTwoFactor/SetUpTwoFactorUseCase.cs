using AppTemplate.Application.Common.Abstractions;
using AppTemplate.Application.Common.Results;
using AppTemplate.Application.Features.Auth.Errors;
using AppTemplate.Application.Features.Auth.Ports.TwoFactorEnrollment;

namespace AppTemplate.Application.Features.Auth.UseCases.Commands.SetUpTwoFactor;

/// <summary>
/// Begins two-factor enrollment: hands back a shared key and an <c>otpauth://</c> URI to scan, but
/// arms nothing yet — see <see cref="Commands.ConfirmTwoFactorSetup"/> for the step that does.
/// <para>
/// No <c>CredentialInvalidationPolicy</c> here, on purpose. The first call for an account provisions a
/// secret, which rotates the security stamp as a side effect of ASP.NET Identity's own
/// <c>ResetAuthenticatorKeyAsync</c> — the caller's own access token stops validating on its very
/// next request, exactly as it would after a password change, and self-heals at the next refresh.
/// Revoking every refresh token on top of that would sign every device the account is signed into
/// out of a step that has not actually changed anything yet: two-factor sign-in stays off until
/// <see cref="Commands.ConfirmTwoFactorSetup"/> proves the caller can produce a code from it.
/// </para>
/// </summary>
public sealed class SetUpTwoFactorUseCase(
    ITwoFactorEnrollmentService enrollment,
    ICurrentUser currentUser) : ISetUpTwoFactorUseCase
{
    public async Task<Result<SetUpTwoFactorOutcome>> ExecuteAsync(CancellationToken cancellationToken = default)
    {
        var userId = currentUser.RequireUserId();

        if (userId.IsFailure)
        {
            return userId.To<SetUpTwoFactorOutcome>();
        }

        var started = await enrollment.BeginAsync(userId.Value, cancellationToken);

        if (started.Status is TwoFactorSetupStatus.AlreadyEnabled)
        {
            return Result.Failure<SetUpTwoFactorOutcome>(AuthErrors.TwoFactorAlreadyEnabled);
        }

        return Result.Success(new SetUpTwoFactorOutcome(started.SharedKey!, started.AuthenticatorUri!));
    }
}
