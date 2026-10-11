using AppTemplate.Application.Core.Common.Results;
using AppTemplate.Application.Core.Common.Validation;

namespace AppTemplate.Application.Auth.Features.Auth.Errors;

/// <summary>
/// The failures this vertical is allowed to disclose: single, stable, deliberately uninformative
/// values, several of them the same value returned for causes the caller must not be able to tell
/// apart.
/// </summary>
public static class AuthErrors
{
    /// <summary>
    /// One error for every login failure — unknown address, wrong password, unconfirmed email,
    /// locked-out account. Even the lockout is not disclosed, since saying so confirms the account.
    /// </summary>
    public static Error InvalidCredentials { get; } = Error.Unauthorized(
        "auth.login.invalidCredentials",
        "Email or password is incorrect.");

    /// <summary>
    /// One error for every way signing in through an identity provider can fail. It collapses as
    /// hard as <see cref="InvalidCredentials"/> does: anyone can obtain an <c>id_token</c> for an
    /// address they control, so a distinct answer for "that address belongs to an unconfirmed local
    /// account" would turn this endpoint into a probe for which addresses are registered here.
    /// </summary>
    public static Error ExternalSignInRefused { get; } = Error.Unauthorized(
        "auth.externalSignIn.refused",
        "The external sign-in could not be completed.");

    /// <summary>
    /// Covers unknown, expired, revoked and replayed refresh tokens alike; a replay additionally
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
    /// The caller named a session that is not one of theirs; also the answer for another account's
    /// session, so the route discloses nothing about which session ids exist.
    /// </summary>
    public static Error NoSuchSession { get; } = Error.NotFound(
        "auth.session.notFound",
        "No such session.");

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

    /// <summary>
    /// The submitted code did not match; attached to "code", unlike <see cref="InvalidCredentials"/>:
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

    /// <summary>
    /// An administrator stripping their own second factor this way would bypass the password proof
    /// the self-service route requires of everyone else — a stolen admin session could otherwise do
    /// to its own account exactly what this feature exists to prevent. <c>ITwoFactorService.DisableAsync</c>
    /// is what an administrator uses on their own account.
    /// </summary>
    public static Error CannotDisableOwnTwoFactor { get; } = Error.Forbidden(
        "auth.twoFactor.cannotTargetSelf",
        "An administrator cannot disable two-factor sign-in on their own account this way.");

    /// <summary>The store refused to provision a secret, so none was handed back. See <see cref="AccountDeletionRejected"/> for why no message is carried.</summary>
    public static Error TwoFactorSetupRejected { get; } = Error.Validation(
        "auth.twoFactor.setupRejected",
        "Two-factor setup could not be started on this account.");

    /// <summary>
    /// The passkey registration ceremony did not verify — a state string this API never issued, a
    /// credential that does not answer it, one already registered, or a state issued for a
    /// different account. Attached to "credential": the caller is authenticated, so there is no
    /// account to protect from enumeration, and nothing safe to echo either.
    /// </summary>
    public static Error InvalidPasskeyRegistration { get; } = ValidationError.ForField(
        "credential",
        "The passkey registration could not be completed.");

    /// <summary>The ceremony verified, but the store refused to hold the credential. See <see cref="AccountDeletionRejected"/> for why no message is carried.</summary>
    public static Error PasskeyRegistrationRejected { get; } = Error.Validation(
        "auth.passkey.registrationRejected",
        "The passkey could not be registered.");

    /// <summary>
    /// The caller named a passkey that is not one of theirs; also the answer for another account's
    /// passkey and for one already removed, so the route discloses nothing about which credential
    /// ids exist.
    /// </summary>
    public static Error NoSuchPasskey { get; } = Error.NotFound(
        "auth.passkey.notFound",
        "No such passkey.");

    /// <summary>
    /// One error for every way a passkey sign-in can fail. A ceremony is cryptographic, so a
    /// distinct answer is not an enumeration oracle the way a wrong password is — but the caller can
    /// do nothing with the distinction either, and the anonymous route keeps one answer for the
    /// same reason <see cref="InvalidCredentials"/> does.
    /// </summary>
    public static Error InvalidPasskeySignIn { get; } = Error.Unauthorized(
        "auth.passkeySignIn.invalid",
        "The passkey sign-in could not be completed.");

    /// <summary>Password and code matched, but the store refused to arm the factor. See <see cref="AccountDeletionRejected"/> for why no message is carried.</summary>
    public static Error TwoFactorEnableRejected { get; } = Error.Validation(
        "auth.twoFactor.enableRejected",
        "Two-factor sign-in could not be enabled on this account.");

    /// <summary>The password matched, but the store refused to disarm the factor. See <see cref="AccountDeletionRejected"/> for why no message is carried.</summary>
    public static Error TwoFactorDisableRejected { get; } = Error.Validation(
        "auth.twoFactor.disableRejected",
        "Two-factor sign-in could not be disabled on this account.");

    /// <summary>The account was found but the store refused the change itself. See <see cref="AccountDeletionRejected"/> for why no message is carried.</summary>
    public static Error TwoFactorAdministrativeDisableRejected { get; } = Error.Validation(
        "auth.twoFactor.administrativeDisableRejected",
        "Two-factor sign-in could not be disabled on this account.");
}
