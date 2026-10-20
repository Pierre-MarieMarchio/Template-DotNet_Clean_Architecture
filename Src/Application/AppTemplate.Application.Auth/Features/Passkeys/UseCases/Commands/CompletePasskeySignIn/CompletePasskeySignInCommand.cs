namespace AppTemplate.Application.Auth.Features.Passkeys.UseCases.Commands.CompletePasskeySignIn;

public sealed record CompletePasskeySignInCommand(string Credential, string AuthenticationState);
