using AppTemplate.Application.Core.Common.Results;
using AppTemplate.Application.Core.Common.Validation;

namespace AppTemplate.Application.Auth.Features.Accounts.Model.Errors;

/// <summary>
/// The failures the accounts feature is allowed to disclose: single, stable, deliberately
/// uninformative values, several of them the same value returned for causes the caller must not be
/// able to tell apart.
/// <para>
/// The current-password pair and the re-authentication demand are also what the passkey and
/// two-factor operations answer with: the proof those operations ask for is the account's, so the
/// answer is the account's too, declared once here.
/// </para>
/// </summary>
public static class AccountsErrors
{
    /// <summary>
    /// Registration cannot fully hide that an address is taken, but it does not have to say so:
    /// neutral and identical for a duplicate username and a duplicate email.
    /// </summary>
    public static Error RegistrationConflict { get; } = Error.Conflict(
        "auth.register.unavailable",
        "That username or email address cannot be used.");

    /// <summary>
    /// Each message describes the submitted value it is attached to — allowed characters, format,
    /// password policy — so it is safe to return verbatim, and attached to the field it concerns
    /// so a client renders it there instead of parsing free text.
    /// </summary>
    public static Error RegistrationRejected(string? userName, string? email, string? password)
    {
        var details = new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal);

        if (userName is not null)
        {
            details["userName"] = [userName];
        }

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

    /// <summary>
    /// The current password matched; the store refused the new one itself. Attached to
    /// "newPassword". See <see cref="RegistrationRejected"/>.
    /// </summary>
    public static Error PasswordChangeRejected(string message) => ValidationError.ForField("newPassword", message);

    /// <summary>Attached to the "currentPassword" field, since it names exactly what the caller typed wrong.</summary>
    public static Error IncorrectCurrentPassword { get; } = ValidationError.ForField(
        "currentPassword",
        "The current password is incorrect.");

    /// <summary>
    /// Identical whether the address is unknown or the token is wrong, so the endpoint cannot be
    /// used to test which addresses are registered.
    /// </summary>
    public static Error InvalidEmailConfirmation { get; } = Error.Validation(
        "auth.confirmEmail.invalid",
        "The confirmation link is invalid or has expired.");

    /// <summary>
    /// Identical whether the address is unknown or the token is wrong or expired, for the same
    /// reason as <see cref="InvalidEmailConfirmation"/>.
    /// </summary>
    public static Error InvalidPasswordReset { get; } = Error.Validation(
        "auth.resetPassword.invalid",
        "The password reset link is invalid or has expired.");

    /// <summary>
    /// The token was valid; the store refused the new password itself. Attached to "newPassword".
    /// See <see cref="RegistrationRejected"/>.
    /// </summary>
    public static Error PasswordResetRejected(string message) => ValidationError.ForField("newPassword", message);

    /// <summary>
    /// Identical for every way a bad token can fail. Not an anti-enumeration measure — the caller
    /// already authenticated as the account it names — simply one error for a bad token.
    /// </summary>
    public static Error InvalidEmailChange { get; } = Error.Validation(
        "auth.changeEmail.invalid",
        "The email change link is invalid or has expired.");

    /// <summary>The token was valid; the store refused the new address itself. See <see cref="RegistrationRejected"/>.</summary>
    public static Error EmailChangeRejected(string message) => ValidationError.ForField("email", message);

    /// <summary>
    /// An administrative action named an id no account carries; not an anti-enumeration measure —
    /// the caller is an administrator acting on an id it already has.
    /// </summary>
    public static Error NoSuchAccount { get; } = Error.NotFound(
        "auth.account.notFound",
        "No such account.");

    /// <summary>
    /// Locking their own account would take effect on the very request asking for it — the
    /// security-stamp rotation would invalidate the token making the call — and no other
    /// administrator would keep a working session to undo it.
    /// </summary>
    public static Error CannotLockOwnAccount { get; } = Error.Forbidden(
        "auth.lockout.cannotTargetSelf",
        "An administrator cannot lock their own account.");

    /// <summary>The account was found but the store refused the lockout change itself. See <see cref="AccountDeletionRejected"/> for why no message is carried.</summary>
    public static Error AccountLockoutRejected { get; } = Error.Validation(
        "auth.lockout.rejected",
        "The lockout change could not be applied.");

    /// <summary>
    /// Removing their own role would revoke the very permission the request is made under, for
    /// the reason <see cref="CannotLockOwnAccount"/> gives.
    /// </summary>
    public static Error CannotRemoveOwnRole { get; } = Error.Forbidden(
        "auth.roles.cannotTargetSelf",
        "An administrator cannot remove a role from their own account.");

    /// <summary>The store refused the assignment or removal itself — an unknown role, or one already in that state.</summary>
    public static Error RoleAssignmentRejected(string message) => ValidationError.ForField("role", message);

    /// <summary>
    /// Deleting their own account is a more permanent version of <see cref="CannotLockOwnAccount"/>'s
    /// problem: nothing survives to undo it.
    /// </summary>
    public static Error CannotDeleteOwnAccount { get; } = Error.Forbidden(
        "auth.account.cannotDeleteSelf",
        "An administrator cannot delete their own account.");

    /// <summary>
    /// The account was found but the store refused to delete it. No message is carried: the failure
    /// describes the store's own state, not something the caller submitted, so there is nothing
    /// safe to echo back.
    /// </summary>
    public static Error AccountDeletionRejected { get; } = Error.Validation(
        "auth.account.deletionRejected",
        "The account could not be deleted.");

    /// <summary>
    /// The account holds no credential its holder could present — no password, no armed second
    /// factor — so the deletion asked for a recently issued access token and this one is older than
    /// the window. Attached to no field: nothing the caller typed is wrong.
    /// </summary>
    public static Error ReAuthenticationRequired { get; } = Error.Forbidden(
        "auth.account.reAuthenticationRequired",
        "Sign in again and retry within five minutes.");

    /// <summary>
    /// The account has a password, so deleting it asks for one; attached to "currentPassword", the
    /// field left empty.
    /// </summary>
    public static Error MissingCurrentPassword { get; } = ValidationError.ForField(
        "currentPassword",
        "The current password is required.");

    /// <summary>
    /// Two-factor sign-in is armed, so deleting the account asks for a code; attached to
    /// "twoFactorCode", the field left empty.
    /// </summary>
    public static Error MissingTwoFactorCode { get; } = ValidationError.ForField(
        "twoFactorCode",
        "A two-factor code is required.");

    /// <summary>
    /// The submitted code did not confirm the deletion; attached to "twoFactorCode". The caller
    /// already authenticated as the account it is deleting, so there is nothing to protect from
    /// enumeration.
    /// </summary>
    public static Error InvalidTwoFactorConfirmationCode { get; } = ValidationError.ForField(
        "twoFactorCode",
        "The verification code is incorrect or has expired.");
}
