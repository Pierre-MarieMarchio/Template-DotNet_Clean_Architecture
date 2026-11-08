namespace AppTemplate.Application.Auth.Features.Passkeys.Ports.Passkeys;

public sealed record PasskeyRegistrationChallenge(string CreationOptions, string RegistrationState);
