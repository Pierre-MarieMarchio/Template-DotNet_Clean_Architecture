namespace AppTemplate.Application.Auth.Features.Passkeys.Ports.Passkeys;

public sealed record PasskeySignInChallenge(string SignInOptions, string AuthenticationState);
