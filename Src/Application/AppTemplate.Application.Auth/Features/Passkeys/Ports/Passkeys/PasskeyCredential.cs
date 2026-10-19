namespace AppTemplate.Application.Auth.Features.Passkeys.Ports.Passkeys;

/// <summary>One of an account's registered passkeys, as its owner sees it in a list.</summary>
/// <param name="CredentialId">The credential's WebAuthn id. Not a <see cref="Guid"/>: a WebAuthn
/// credential id is up to 1023 opaque bytes, and the client that lists passkeys is the client that
/// addresses one for removal, so what it carries is the id in the wire's own encoding.</param>
/// <param name="Name">The label the holder gave the credential when registering it, or
/// <c>null</c> when none was given.</param>
/// <param name="CreatedAt">When the credential was registered.</param>
public sealed record PasskeyCredential(byte[] CredentialId, string? Name, DateTimeOffset CreatedAt);
