namespace AppTemplate.Application.Auth.Features.AccessKeys.Ports.AccessKeys;

public sealed record LiveAccessKey(
    Guid Id,
    string Name,
    string Prefix,
    IReadOnlyList<string> Scopes,
    DateTimeOffset CreatedAt,
    DateTimeOffset? LastUsedAt,
    DateTimeOffset? ExpiresAt);
