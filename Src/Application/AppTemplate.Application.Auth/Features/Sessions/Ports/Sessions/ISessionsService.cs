namespace AppTemplate.Application.Auth.Features.Sessions.Ports.Sessions;

public interface ISessionsService
{
    Task<IReadOnlyList<ActiveSession>> ListActiveAsync(Guid userId, CancellationToken cancellationToken = default);

    Task<bool> RevokeAsync(Guid userId, Guid sessionId, CancellationToken cancellationToken = default);
}
