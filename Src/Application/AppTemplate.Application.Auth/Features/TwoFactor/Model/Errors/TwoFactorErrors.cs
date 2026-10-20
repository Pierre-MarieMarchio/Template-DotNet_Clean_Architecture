using AppTemplate.Application.Core.Common.Results;
using AppTemplate.Application.Core.Common.Validation;

namespace AppTemplate.Application.Auth.Features.TwoFactor.Model.Errors;

public static class TwoFactorErrors
{
    public static Error InvalidTwoFactorCode { get; } = ValidationError.ForField(
        "code",
        "The verification code is incorrect or has expired.");

    public static Error TwoFactorAlreadyEnabled { get; } = Error.Conflict(
        "auth.twoFactor.alreadyEnabled",
        "Two-factor authentication is already enabled on this account.");

    public static Error InvalidTwoFactorChallenge { get; } = Error.Unauthorized(
        "auth.login.invalidTwoFactorChallenge",
        "The two-factor challenge is invalid or has expired.");

    public static Error CannotDisableOwnTwoFactor { get; } = Error.Forbidden(
        "auth.twoFactor.cannotTargetSelf",
        "An administrator cannot disable two-factor sign-in on their own account this way.");

    public static Error TwoFactorSetupRejected { get; } = Error.Validation(
        "auth.twoFactor.setupRejected",
        "Two-factor setup could not be started on this account.");

    public static Error TwoFactorEnableRejected { get; } = Error.Validation(
        "auth.twoFactor.enableRejected",
        "Two-factor sign-in could not be enabled on this account.");

    public static Error TwoFactorDisableRejected { get; } = Error.Validation(
        "auth.twoFactor.disableRejected",
        "Two-factor sign-in could not be disabled on this account.");

    public static Error TwoFactorAdministrativeDisableRejected { get; } = Error.Validation(
        "auth.twoFactor.administrativeDisableRejected",
        "Two-factor sign-in could not be disabled on this account.");
}
