namespace AppTemplate.Api.Features.Auth.Contracts.Responses;

/// <summary>One of the caller's live access keys, as the access key list shows it.</summary>
/// <param name="Id">The key's id: what the revoke route takes.</param>
/// <param name="Name">The label it was given.</param>
/// <param name="Prefix">The key's first characters, to tell keys apart; never the key.</param>
/// <param name="Scopes">What the key may do.</param>
/// <param name="CreatedAt">When it was created.</param>
/// <param name="LastUsedAt">When it last authenticated a caller, to the minute, or <c>null</c> when it never did.</param>
/// <param name="ExpiresAt">When it stops working, or <c>null</c> when it lives until revoked.</param>
public sealed record AccessKeyResponse(
    Guid Id,
    string Name,
    string Prefix,
    IReadOnlyList<string> Scopes,
    DateTimeOffset CreatedAt,
    DateTimeOffset? LastUsedAt,
    DateTimeOffset? ExpiresAt);
