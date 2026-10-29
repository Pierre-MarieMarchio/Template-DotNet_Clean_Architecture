namespace AppTemplate.Api.Features.Auth.Contracts.Requests;

/// <summary>A name, the scopes, an optional expiry and the current password for a new access key.</summary>
/// <param name="Name">A label for the key, shown in the access key list.</param>
/// <param name="Scopes">What the key may do: <c>read</c>, <c>write</c>, or both.</param>
/// <param name="ExpiresAt">When the key stops working, in the future; <c>null</c> for a key that lives until revoked.</param>
/// <param name="CurrentPassword">The account's password.</param>
public sealed record CreateAccessKeyRequest(
    string Name,
    IReadOnlyList<string> Scopes,
    DateTimeOffset? ExpiresAt,
    string? CurrentPassword);
