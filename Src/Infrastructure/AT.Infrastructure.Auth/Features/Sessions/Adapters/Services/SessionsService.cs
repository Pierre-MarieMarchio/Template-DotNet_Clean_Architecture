using AppTemplate.Application.Auth.Features.Sessions.Ports.Sessions;
using AppTemplate.Infrastructure.Auth.Common.Contexts;
using AppTemplate.Infrastructure.Auth.Features.Sessions.Access.Tables;
using AppTemplate.Infrastructure.Core.Common.Saving;

namespace AppTemplate.Infrastructure.Auth.Features.Sessions.Adapters.Services;

internal sealed class SessionsService(
    IRefreshTokenFamilyTable families,
    IContextUnitOfWork<AuthDbContext> unitOfWork,
    TimeProvider timeProvider) : ISessionsService
{
    public async Task<IReadOnlyList<ActiveSession>> ListActiveAsync(
        Guid userId,
        CancellationToken cancellationToken = default) => await families.ListLiveAsync(userId, timeProvider.GetUtcNow(), cancellationToken);

    public async Task<bool> RevokeAsync(
        Guid userId,
        Guid sessionId,
        CancellationToken cancellationToken = default)
    {
        bool ended = await families.EndAsync(userId, sessionId, timeProvider.GetUtcNow(), cancellationToken);

        await unitOfWork.SaveChangesAsync(cancellationToken);

        return ended;
    }
}
