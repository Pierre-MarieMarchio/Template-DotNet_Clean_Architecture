using AppTemplate.Application.Auth.Features.Auth.Ports.CurrentSession;
using AppTemplate.Application.Auth.Features.Auth.Ports.Sessions;
using AppTemplate.Application.Core.Common.Ports;
using AppTemplate.Application.Core.Common.Results;

namespace AppTemplate.Application.Auth.Features.Auth.UseCases.Queries.ListSessions;

/// <summary>
/// Lists the caller's own live sessions and marks the one the request comes from, so a client can
/// show "this device" and offer to end the others.
/// </summary>
public sealed class ListSessionsUseCase(
    ISessionsService sessions,
    ICurrentUser currentUser,
    ICurrentSession currentSession) : IListSessionsUseCase
{
    /// <inheritdoc />
    public async Task<Result<IReadOnlyList<ListSessionsItem>>> ExecuteAsync(
        CancellationToken cancellationToken = default)
    {
        var userId = currentUser.RequireUserId();

        if (userId.IsFailure)
        {
            return userId.To<IReadOnlyList<ListSessionsItem>>();
        }

        var active = await sessions.ListActiveAsync(userId.Value.Value, cancellationToken);
        var current = currentSession.SessionId;

        IReadOnlyList<ListSessionsItem> items =
        [
            .. active.Select(session => new ListSessionsItem(
                session.Id,
                session.CreatedAt,
                session.LastUsedAt,
                session.UserAgent,
                Current: session.Id == current)),
        ];

        return Result.Success(items);
    }
}
