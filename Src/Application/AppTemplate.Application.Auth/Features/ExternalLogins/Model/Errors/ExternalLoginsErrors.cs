using AppTemplate.Application.Auth.Features.Sessions.Model.Errors;
using AppTemplate.Application.Core.Common.Results;

namespace AppTemplate.Application.Auth.Features.ExternalLogins.Model.Errors;

/// <summary>
/// The failure signing in through an identity provider is allowed to disclose: one value, whatever
/// the cause.
/// </summary>
public static class ExternalLoginsErrors
{
    /// <summary>One error for every way signing in through an identity provider can fail. It collapses
    /// as hard as <see cref="SessionsErrors.InvalidCredentials"/> does, or the endpoint becomes a probe for which
    /// addresses are registered here.</summary>
    public static Error ExternalSignInRefused { get; } = Error.Unauthorized(
        "auth.externalSignIn.refused",
        "The external sign-in could not be completed.");
}
