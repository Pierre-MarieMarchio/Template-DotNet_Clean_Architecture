namespace AppTemplate.Application.Auth.Features.AccessKeys.Ports.AccessKeys;

public sealed record AccessKeyGrant(
    Guid AccessKeyId,
    Guid UserId,
    IReadOnlyList<string> Scopes,
    DateTimeOffset? ExpiresAt,
    DateTimeOffset? RevokedAt,
    DateTimeOffset? LastUsedAt)
{
    public bool IsLiveAt(DateTimeOffset instant) => RevokedAt is null && (ExpiresAt is null || ExpiresAt > instant);
}
