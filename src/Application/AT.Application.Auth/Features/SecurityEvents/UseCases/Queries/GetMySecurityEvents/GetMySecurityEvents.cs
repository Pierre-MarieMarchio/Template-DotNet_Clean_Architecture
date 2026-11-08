using AppTemplate.Application.Auth.Features.SecurityEvents.Model.Policies;
using AppTemplate.Application.Auth.Features.SecurityEvents.Ports.SecurityEventQueries;
using AppTemplate.Application.Core.Common.Collections;
using AppTemplate.Application.Core.Common.Ports;
using AppTemplate.Application.Core.Common.Results;
using Mediator;

namespace AppTemplate.Application.Auth.Features.SecurityEvents.UseCases.Queries.GetMySecurityEvents;

public sealed record GetMySecurityEventsQuery(int? Page, int? PageSize) : IQuery<Result<PagedResult<SecurityEventEntry>>>;

public sealed class GetMySecurityEventsQueryHandler(
    ISecurityEventQueries queries,
    ICurrentUser currentUser) : IQueryHandler<GetMySecurityEventsQuery, Result<PagedResult<SecurityEventEntry>>>
{
    public async ValueTask<Result<PagedResult<SecurityEventEntry>>> Handle(
        GetMySecurityEventsQuery query,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        var userId = currentUser.RequireUserId();

        if (userId.IsFailure)
        {
            return userId.To<PagedResult<SecurityEventEntry>>();
        }

        var paging = PageRequest.Create(
            PagingMode.Offset,
            query.Page,
            query.PageSize,
            cursor: null,
            SecurityEventCollectionPolicy.Instance);

        if (paging.IsFailure)
        {
            return paging.To<PagedResult<SecurityEventEntry>>();
        }

        return await queries.GetForUserAsync(userId.Value.Value, paging.Value, cancellationToken);
    }
}
