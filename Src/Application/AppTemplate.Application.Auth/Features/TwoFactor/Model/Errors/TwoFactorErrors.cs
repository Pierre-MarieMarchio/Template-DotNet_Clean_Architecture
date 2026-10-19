using AppTemplate.Application.Auth.Features.Accounts.Model.Errors;
using AppTemplate.Application.Auth.Features.Sessions.Model.Errors;
using AppTemplate.Application.Core.Common.Results;
using AppTemplate.Application.Core.Common.Validation;

namespace AppTemplate.Application.Auth.Features.TwoFactor.Model.Errors;

/// <summary>
/// The failures the two-factor feature is allowed to disclose: one value per refusal, the
/// challenge's collapsed so a holder of a stolen challenge learns nothing from it.
/// </summary>
public static class TwoFactorErrors
{
    /// <summary>
    /// The submitted code did not match; attached to "code", unlike <see cref="SessionsErrors.InvalidCredentials"/>:
    /// the caller already authenticated, so there is no address to protect from enumeration.
    /// </summary>
    public static Error InvalidTwoFactorCode { get; } = ValidationError.ForField(
        "code",
        "The verification code is incorrect or has expired.");

    /// <summary>
    /// Two-factor sign-in is already active; provisioning a fresh secret on top would silently swap
    /// the key every authenticator app already on file was built from — see
    /// <c>SetUpTwoFactorUseCase</c> for why that is refused.
    /// </summary>
    public static Error TwoFactorAlreadyEnabled { get; } = Error.Conflict(
        "auth.twoFactor.alreadyEnabled",
        "Two-factor authentication is already enabled on this account.");

    /// <summary>
    /// Covers an unknown, expired, redeemed challenge and a wrong code alike: telling the two apart
    /// would tell a holder of a stolen challenge token whether the code it tried was merely wrong
    /// or the challenge itself was already spent.
    /// </summary>
    public static Error InvalidTwoFactorChallenge { get; } = Error.Unauthorized(
        "auth.login.invalidTwoFactorChallenge",
        "The two-factor challenge is invalid or has expired.");

    /// <summary>An administrator stripping their own second factor this way would bypass the password
    /// proof the self-service route requires of everyone else; <c>ITwoFactorService.DisableAsync</c>
    /// is what an administrator uses on their own account.</summary>
    public static Error CannotDisableOwnTwoFactor { get; } = Error.Forbidden(
        "auth.twoFactor.cannotTargetSelf",
        "An administrator cannot disable two-factor sign-in on their own account this way.");

    /// <summary>The store refused to provision a secret, so none was handed back. See <see cref="AccountsErrors.AccountDeletionRejected"/> for why no message is carried.</summary>
    public static Error TwoFactorSetupRejected { get; } = Error.Validation(
        "auth.twoFactor.setupRejected",
        "Two-factor setup could not be started on this account.");

    /// <summary>Password and code matched, but the store refused to arm the factor. See <see cref="AccountsErrors.AccountDeletionRejected"/> for why no message is carried.</summary>
    public static Error TwoFactorEnableRejected { get; } = Error.Validation(
        "auth.twoFactor.enableRejected",
        "Two-factor sign-in could not be enabled on this account.");

    /// <summary>The password matched, but the store refused to disarm the factor. See <see cref="AccountsErrors.AccountDeletionRejected"/> for why no message is carried.</summary>
    public static Error TwoFactorDisableRejected { get; } = Error.Validation(
        "auth.twoFactor.disableRejected",
        "Two-factor sign-in could not be disabled on this account.");

    /// <summary>The account was found but the store refused the change itself. See <see cref="AccountsErrors.AccountDeletionRejected"/> for why no message is carried.</summary>
    public static Error TwoFactorAdministrativeDisableRejected { get; } = Error.Validation(
        "auth.twoFactor.administrativeDisableRejected",
        "Two-factor sign-in could not be disabled on this account.");
}
