namespace AppTemplate.Application.Auth.Features.Passkeys.UseCases.Commands.RemovePasskey;

public sealed record RemovePasskeyCommand(byte[] CredentialId);
