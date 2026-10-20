using AppTemplate.Application.Core.Common.Results;

namespace AppTemplate.Application.Auth.Features.ExternalLogins.Model.Errors;

public static class ExternalLoginsErrors
{
    public static Error ExternalSignInRefused { get; } = Error.Unauthorized(
        "auth.externalSignIn.refused",
        "The external sign-in could not be completed.");
}
