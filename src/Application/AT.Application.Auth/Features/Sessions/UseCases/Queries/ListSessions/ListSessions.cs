using AppTemplate.Application.Auth.Features.Sessions.Ports.CurrentSession;
using AppTemplate.Application.Auth.Features.Sessions.Ports.Sessions;
using AppTemplate.Application.Core.Common.Ports;
using AppTemplate.Application.Core.Common.Results;
using Mediator;

namespace AppTemplate.Application.Auth.Features.Sessions.UseCases.Queries.ListSessions;

public sealed record ListSessionsQuery : IQuery<Result<IReadOnlyList<ListSessionsItem>>>;

public sealed class ListSessionsQueryHandler(
    ISessionsService sessions,
    ICurrentUser currentUser,
    ICurrentSession currentSession) : IQueryHandler<ListSessionsQuery, Result<IReadOnlyList<ListSessionsItem>>>
{
    public async ValueTask<Result<IReadOnlyList<ListSessionsItem>>> Handle(
        ListSessionsQuery query,
        CancellationToken cancellationToken)
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
