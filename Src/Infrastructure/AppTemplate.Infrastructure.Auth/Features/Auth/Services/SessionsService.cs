using AppTemplate.Application.Auth.Features.Sessions.Ports.Sessions;
using AppTemplate.Application.Core.Common.Ports;
using AppTemplate.Infrastructure.Auth.Common.Contexts;
using AppTemplate.Infrastructure.Auth.Features.Auth.Tables;
using AppTemplate.Infrastructure.Core.Common.Saving;

namespace AppTemplate.Infrastructure.Auth.Features.Auth.Services;

/// <summary>
/// <see cref="ISessionsService"/> over <see cref="IRefreshTokenFamilyTable"/>: a session is one
/// sign-in's refresh-token family, so listing and ending sessions is reading and ending family rows.
/// <para>
/// This adapter never touches a token value — that half of a session belongs to
/// <see cref="SessionTokensService"/> and stays there.
/// </para>
/// </summary>
internal sealed class SessionsService(
    IRefreshTokenFamilyTable families,
    IContextUnitOfWork<AuthDbContext> unitOfWork,
    IDateTimeProvider dateTimeProvider) : ISessionsService
{
    public async Task<IReadOnlyList<ActiveSession>> ListActiveAsync(
        Guid userId,
        CancellationToken cancellationToken = default)
    {
        return await families.ListLiveAsync(userId, dateTimeProvider.UtcNow, cancellationToken);
    }

    public async Task<bool> RevokeAsync(
        Guid userId,
        Guid sessionId,
        CancellationToken cancellationToken = default)
    {
        // EndAsync's own statements commit on their own — a revocation has to survive a caller that
        // walks away — so this commits anything else the request staged rather than the revocation
        // itself, and is a no-op when there is none.
        bool ended = await families.EndAsync(userId, sessionId, dateTimeProvider.UtcNow, cancellationToken);

        await unitOfWork.SaveChangesAsync(cancellationToken);

        return ended;
    }
}
