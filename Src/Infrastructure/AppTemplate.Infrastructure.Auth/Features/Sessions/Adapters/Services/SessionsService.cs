using AppTemplate.Application.Auth.Features.Sessions.Ports.Sessions;
using AppTemplate.Application.Core.Common.Ports;
using AppTemplate.Infrastructure.Auth.Common.Contexts;
using AppTemplate.Infrastructure.Auth.Features.Sessions.Access.Tables;
using AppTemplate.Infrastructure.Core.Common.Saving;

namespace AppTemplate.Infrastructure.Auth.Features.Sessions.Adapters.Services;

internal sealed class SessionsService(
    IRefreshTokenFamilyTable families,
    IContextUnitOfWork<AuthDbContext> unitOfWork,
    IDateTimeProvider dateTimeProvider) : ISessionsService
{
    public async Task<IReadOnlyList<ActiveSession>> ListActiveAsync(
        Guid userId,
        CancellationToken cancellationToken = default) => await families.ListLiveAsync(userId, dateTimeProvider.UtcNow, cancellationToken);

    public async Task<bool> RevokeAsync(
        Guid userId,
        Guid sessionId,
        CancellationToken cancellationToken = default)
    {
        bool ended = await families.EndAsync(userId, sessionId, dateTimeProvider.UtcNow, cancellationToken);

        await unitOfWork.SaveChangesAsync(cancellationToken);

        return ended;
    }
}
