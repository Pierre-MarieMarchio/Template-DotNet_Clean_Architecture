using AppTemplate.Application.Core.Common.Results;
using AppTemplate.Application.Core.Common.Validation;

namespace AppTemplate.Application.Auth.Features.Accounts.Model.Errors;

public static class AccountsErrors
{
    public static Error RegistrationRejected(string? email, string? password)
    {
        var details = new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal);

        if (email is not null)
        {
            details["email"] = [email];
        }

        if (password is not null || details.Count == 0)
        {
            details["password"] = [password ?? "The submitted password does not meet the required policy."];
        }

        return ValidationError.ForFields(details);
    }

    public static Error PasswordChangeRejected(string message) => ValidationError.ForField("newPassword", message);

    public static Error IncorrectCurrentPassword { get; } = ValidationError.ForField(
        "currentPassword",
        "The current password is incorrect.");

    public static Error InvalidEmailConfirmation { get; } = Error.Validation(
        "auth.confirmEmail.invalid",
        "The confirmation link is invalid or has expired.");

    public static Error InvalidPasswordReset { get; } = Error.Validation(
        "auth.resetPassword.invalid",
        "The password reset link is invalid or has expired.");

    public static Error PasswordResetRejected(string message) => ValidationError.ForField("newPassword", message);

    public static Error InvalidEmailChange { get; } = Error.Validation(
        "auth.changeEmail.invalid",
        "The email change link is invalid or has expired.");

    public static Error EmailChangeRejected(string message) => ValidationError.ForField("email", message);

    public static Error NoSuchAccount { get; } = Error.NotFound(
        "auth.account.notFound",
        "No such account.");

    public static Error CannotLockOwnAccount { get; } = Error.Forbidden(
        "auth.lockout.cannotTargetSelf",
        "An administrator cannot lock their own account.");

    public static Error AccountLockoutRejected { get; } = Error.Validation(
        "auth.lockout.rejected",
        "The lockout change could not be applied.");

    public static Error CannotRemoveOwnRole { get; } = Error.Forbidden(
        "auth.roles.cannotTargetSelf",
        "An administrator cannot remove a role from their own account.");

    public static Error RoleAssignmentRejected(string message) => ValidationError.ForField("role", message);

    public static Error CannotDeleteOwnAccount { get; } = Error.Forbidden(
        "auth.account.cannotDeleteSelf",
        "An administrator cannot delete their own account.");

    public static Error AccountDeletionRejected { get; } = Error.Validation(
        "auth.account.deletionRejected",
        "The account could not be deleted.");

    public static Error ReAuthenticationRequired { get; } = Error.Forbidden(
        "auth.account.reAuthenticationRequired",
        "Sign in again and retry within five minutes.");

    public static Error MissingCurrentPassword { get; } = ValidationError.ForField(
        "currentPassword",
        "The current password is required.");

    public static Error MissingTwoFactorCode { get; } = ValidationError.ForField(
        "twoFactorCode",
        "A two-factor code is required.");

    public static Error InvalidTwoFactorConfirmationCode { get; } = ValidationError.ForField(
        "twoFactorCode",
        "The verification code is incorrect or has expired.");
}
