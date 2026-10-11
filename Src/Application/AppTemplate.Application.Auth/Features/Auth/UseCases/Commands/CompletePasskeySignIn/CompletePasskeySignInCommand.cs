namespace AppTemplate.Application.Auth.Features.Auth.UseCases.Commands.CompletePasskeySignIn;

/// <param name="Credential">The JSON-serialised result of <c>navigator.credentials.get()</c>, as the
/// client's browser produced it.</param>
/// <param name="AuthenticationState">The opaque string the sign-in options step returned, which
/// the ceremony verifies the assertion against.</param>
public sealed record CompletePasskeySignInCommand(string Credential, string AuthenticationState);
