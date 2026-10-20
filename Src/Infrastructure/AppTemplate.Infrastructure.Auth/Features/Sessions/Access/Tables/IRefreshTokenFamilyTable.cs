using AppTemplate.Application.Auth.Features.Sessions.Ports.Sessions;

namespace AppTemplate.Infrastructure.Auth.Features.Sessions.Access.Tables;

public interface IRefreshTokenFamilyTable
{
    Guid Start(Guid userId, DateTimeOffset createdAt, string? userAgent);

    Task MarkUsedAsync(Guid familyId, string? userAgent, DateTimeOffset now, CancellationToken cancellationToken = default);

    Task<bool> EndAsync(
        Guid userId,
        Guid familyId,
        DateTimeOffset revokedAt,
        CancellationToken cancellationToken = default);

    Task RevokeAllForUserAsync(
        Guid userId,
        Guid? exceptFamilyId,
        DateTimeOffset revokedAt,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ActiveSession>> ListLiveAsync(Guid userId, DateTimeOffset asOf, CancellationToken cancellationToken = default);

    Task<int> PurgeChildlessAsync(CancellationToken cancellationToken = default);
}
