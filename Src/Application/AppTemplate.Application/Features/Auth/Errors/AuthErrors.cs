using AppTemplate.Application.Common;
using AppTemplate.Application.Common.Validation;

namespace AppTemplate.Application.Features.Auth.Errors;

/// <summary>
/// The failures this vertical is allowed to disclose. Each is a single, stable, deliberately
/// uninformative value, and several of them are the same value returned for causes the caller must
/// not be able to tell apart.
/// </summary>
public static class AuthErrors
{
    /// <summary>
    /// One error for every login failure: unknown address, wrong password, unconfirmed email,
    /// locked-out account. Even the lockout is not disclosed, since saying so confirms the account.
    /// </summary>
    public static Error InvalidCredentials { get; } = Error.Unauthorized(
        "auth.login.invalidCredentials",
        "Email or password is incorrect.");

    /// <summary>
    /// Covers unknown, expired, revoked and replayed refresh tokens alike. A replay additionally
    /// revokes the whole family, but the response is identical.
    /// </summary>
    public static Error InvalidRefreshToken { get; } = Error.Unauthorized(
        "auth.refreshToken.invalid",
        "The refresh token is invalid or has expired.");

    /// <summary>
    /// Identical whether the address is unknown or the token is wrong, so the endpoint cannot be
    /// used to test which addresses are registered.
    /// </summary>
    public static Error InvalidEmailConfirmation { get; } = Error.Validation(
        "auth.confirmEmail.invalid",
        "The confirmation link is invalid or has expired.");

    /// <summary>
    /// Registration cannot fully hide that an address is taken, but it does not have to say so:
    /// the message is neutral and identical for a duplicate username and a duplicate email.
    /// </summary>
    public static Error RegistrationConflict { get; } = Error.Conflict(
        "auth.register.unavailable",
        "That username or email address cannot be used.");

    /// <summary>
    /// <paramref name="message"/> describes the submitted values — password policy, allowed
    /// characters, format — so it is safe, and useful, to return verbatim. Attached to the
    /// "password" field rather than used as the error message itself, so a client can render it
    /// next to the field it concerns instead of parsing free text.
    /// </summary>
    public static Error RegistrationRejected(string message) => ValidationError.ForField("password", message);

    /// <summary>Attached to the "currentPassword" field, since it names exactly what the caller typed wrong.</summary>
    public static Error IncorrectCurrentPassword { get; } = ValidationError.ForField(
        "currentPassword",
        "The current password is incorrect.");

    /// <summary>
    /// Identical whether the address is unknown or the token is wrong or expired, for the same
    /// reason as <see cref="InvalidEmailConfirmation"/>.
    /// </summary>
    public static Error InvalidPasswordReset { get; } = Error.Validation(
        "auth.resetPassword.invalid",
        "The password reset link is invalid or has expired.");

    /// <summary>The token was valid; the store refused the new password itself. See <see cref="RegistrationRejected"/>.</summary>
    public static Error PasswordResetRejected(string message) => ValidationError.ForField("password", message);

    /// <summary>
    /// Identical whether the token is unknown, expired, already used, or issued for a different
    /// address. Unlike <see cref="InvalidEmailConfirmation"/> this is not an anti-enumeration
    /// measure — the caller already authenticated as the account it names — it is simply one error
    /// for every way a bad token can fail.
    /// </summary>
    public static Error InvalidEmailChange { get; } = Error.Validation(
        "auth.changeEmail.invalid",
        "The email change link is invalid or has expired.");

    /// <summary>The token was valid; the store refused the new address itself. See <see cref="RegistrationRejected"/>.</summary>
    public static Error EmailChangeRejected(string message) => ValidationError.ForField("email", message);

    /// <summary>
    /// An administrative action named an id no account carries. Unlike <see cref="InvalidCredentials"/>
    /// this is not an anti-enumeration measure: the caller is an authenticated administrator acting on
    /// an id it already has from a user list, not an anonymous caller probing for one.
    /// </summary>
    public static Error NoSuchAccount { get; } = Error.NotFound(
        "auth.account.notFound",
        "No such account.");

    /// <summary>
    /// An administrator locking their own account would take effect on the very request that asked
    /// for it — the security-stamp rotation the lock requires would invalidate the token making the
    /// call — and there would be no other administrator left with a working session to undo it.
    /// </summary>
    public static Error CannotLockOwnAccount { get; } = Error.Forbidden(
        "auth.lockout.cannotTargetSelf",
        "An administrator cannot lock their own account.");

    /// <summary>The account was found but the store refused the lockout change itself. See <see cref="AccountDeletionRejected"/> for why no message is carried.</summary>
    public static Error AccountLockoutRejected { get; } = Error.Validation(
        "auth.lockout.rejected",
        "The lockout change could not be applied.");

    /// <summary>
    /// An administrator removing their own role would revoke the very permission the request is
    /// being made under, for the reason <see cref="CannotLockOwnAccount"/> gives.
    /// </summary>
    public static Error CannotRemoveOwnRole { get; } = Error.Forbidden(
        "auth.roles.cannotTargetSelf",
        "An administrator cannot remove a role from their own account.");

    /// <summary>The store refused the assignment or removal itself — an unknown role, or one already in that state.</summary>
    public static Error RoleAssignmentRejected(string message) => ValidationError.ForField("role", message);

    /// <summary>
    /// An administrator deleting their own account is a more permanent version of
    /// <see cref="CannotLockOwnAccount"/>'s problem: nothing survives to undo it.
    /// </summary>
    public static Error CannotDeleteOwnAccount { get; } = Error.Forbidden(
        "auth.account.cannotDeleteSelf",
        "An administrator cannot delete their own account.");

    /// <summary>
    /// The account was found but the store refused to delete it. No message is carried: unlike a
    /// rejected password or role name, a deletion failure describes the store's own state, not
    /// something the caller submitted, so there is nothing safe to echo back.
    /// </summary>
    public static Error AccountDeletionRejected { get; } = Error.Validation(
        "auth.account.deletionRejected",
        "The account could not be deleted.");

    /// <summary>
    /// The submitted code did not match. Attached to "code", unlike <see cref="InvalidCredentials"/>:
    /// the caller already authenticated as the account it is arming, so there is no address to
    /// protect from enumeration here.
    /// </summary>
    public static Error InvalidTwoFactorCode { get; } = ValidationError.ForField(
        "code",
        "The verification code is incorrect or has expired.");

    /// <summary>
    /// Two-factor sign-in is already active. Provisioning a fresh secret on top of it would silently
    /// swap the key every authenticator app already on file was built from — see
    /// <c>SetUpTwoFactorUseCase</c> for why that is refused rather than allowed.
    /// </summary>
    public static Error TwoFactorAlreadyEnabled { get; } = Error.Conflict(
        "auth.twoFactor.alreadyEnabled",
        "Two-factor authentication is already enabled on this account.");

    /// <summary>
    /// Covers an unknown, expired or already-redeemed challenge and a wrong code alike. Telling the
    /// two apart would tell a caller holding a stolen challenge token whether the code it tried was
    /// merely wrong or the challenge itself was already spent — the same reason
    /// <see cref="InvalidCredentials"/> collapses every login refusal into one answer.
    /// </summary>
    public static Error InvalidTwoFactorChallenge { get; } = Error.Unauthorized(
        "auth.login.invalidTwoFactorChallenge",
        "The two-factor challenge is invalid or has expired.");
}
