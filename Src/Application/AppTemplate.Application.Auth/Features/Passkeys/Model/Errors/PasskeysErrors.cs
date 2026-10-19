using AppTemplate.Application.Auth.Features.Accounts.Model.Errors;
using AppTemplate.Application.Auth.Features.Sessions.Model.Errors;
using AppTemplate.Application.Core.Common.Results;
using AppTemplate.Application.Core.Common.Validation;

namespace AppTemplate.Application.Auth.Features.Passkeys.Model.Errors;

/// <summary>
/// The failures the passkey feature is allowed to disclose: one value per refusal, the sign-in's
/// collapsed as the password sign-in's is.
/// </summary>
public static class PasskeysErrors
{
    /// <summary>The passkey registration ceremony did not verify — a state this API never issued, a
    /// credential that does not answer it, one already registered, or a state issued for a different
    /// account.</summary>
    public static Error InvalidPasskeyRegistration { get; } = ValidationError.ForField(
        "credential",
        "The passkey registration could not be completed.");

    /// <summary>The ceremony verified, but the store refused to hold the credential. See <see cref="AccountsErrors.AccountDeletionRejected"/> for why no message is carried.</summary>
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

    /// <summary>One error for every way a passkey sign-in can fail: a ceremony is cryptographic, so a
    /// distinct answer is no enumeration oracle, but the caller can do nothing with the distinction
    /// either — one answer, as <see cref="SessionsErrors.InvalidCredentials"/> keeps.</summary>
    public static Error InvalidPasskeySignIn { get; } = Error.Unauthorized(
        "auth.passkeySignIn.invalid",
        "The passkey sign-in could not be completed.");
}
