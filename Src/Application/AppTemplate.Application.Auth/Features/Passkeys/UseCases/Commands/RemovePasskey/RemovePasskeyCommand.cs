namespace AppTemplate.Application.Auth.Features.Passkeys.UseCases.Commands.RemovePasskey;

/// <summary>Carries no identity: the account is the authenticated caller's own.</summary>
/// <param name="CredentialId">One of the caller's passkeys, as the passkey list names it.</param>
public sealed record RemovePasskeyCommand(byte[] CredentialId);
