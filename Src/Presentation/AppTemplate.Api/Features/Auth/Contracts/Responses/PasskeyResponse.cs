namespace AppTemplate.Api.Features.Auth.Contracts.Responses;

/// <summary>One of the account's registered passkeys, as the passkey list shows it.</summary>
/// <param name="Id">The credential's WebAuthn id, base64url-encoded: what the removal route takes.</param>
/// <param name="Name">The label its holder gave it when registering, or <c>null</c> when none.</param>
/// <param name="CreatedAt">When it was registered.</param>
public sealed record PasskeyResponse(string Id, string? Name, DateTimeOffset CreatedAt);
