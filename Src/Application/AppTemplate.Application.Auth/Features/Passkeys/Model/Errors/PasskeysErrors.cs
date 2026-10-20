using AppTemplate.Application.Core.Common.Results;
using AppTemplate.Application.Core.Common.Validation;

namespace AppTemplate.Application.Auth.Features.Passkeys.Model.Errors;

public static class PasskeysErrors
{
    public static Error InvalidPasskeyRegistration { get; } = ValidationError.ForField(
        "credential",
        "The passkey registration could not be completed.");

    public static Error PasskeyRegistrationRejected { get; } = Error.Validation(
        "auth.passkey.registrationRejected",
        "The passkey could not be registered.");

    public static Error NoSuchPasskey { get; } = Error.NotFound(
        "auth.passkey.notFound",
        "No such passkey.");

    public static Error InvalidPasskeySignIn { get; } = Error.Unauthorized(
        "auth.passkeySignIn.invalid",
        "The passkey sign-in could not be completed.");
}
