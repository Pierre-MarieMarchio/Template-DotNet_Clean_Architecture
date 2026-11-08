namespace AppTemplate.Api.Features.Auth.Contracts.Responses;

/// <summary>A new access key, with its secret: the only response that ever carries it.</summary>
/// <param name="Id">The key's id: what the revoke route takes.</param>
/// <param name="Name">The label it was given.</param>
/// <param name="Prefix">The key's first characters, which the list shows to tell keys apart.</param>
/// <param name="Scopes">What the key may do.</param>
/// <param name="CreatedAt">When it was created.</param>
/// <param name="ExpiresAt">When it stops working, or <c>null</c> when it lives until revoked.</param>
/// <param name="Key">The key itself. Only its hash is stored: copy it now, it is never shown again.</param>
public sealed record CreatedAccessKeyResponse(
    Guid Id,
    string Name,
    string Prefix,
    IReadOnlyList<string> Scopes,
    DateTimeOffset CreatedAt,
    DateTimeOffset? ExpiresAt,
    string Key);
