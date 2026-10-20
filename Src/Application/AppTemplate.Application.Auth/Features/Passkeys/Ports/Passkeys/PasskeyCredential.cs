namespace AppTemplate.Application.Auth.Features.Passkeys.Ports.Passkeys;

public sealed record PasskeyCredential(byte[] CredentialId, string? Name, DateTimeOffset CreatedAt);
