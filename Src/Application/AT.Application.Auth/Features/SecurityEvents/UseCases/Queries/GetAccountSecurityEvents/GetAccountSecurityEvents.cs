using AppTemplate.Application.Auth.Features.SecurityEvents.Model.Policies;
using AppTemplate.Application.Auth.Features.SecurityEvents.Ports.SecurityEventQueries;
using AppTemplate.Application.Core.Common.Collections;
using AppTemplate.Application.Core.Common.Results;
using Mediator;

namespace AppTemplate.Application.Auth.Features.SecurityEvents.UseCases.Queries.GetAccountSecurityEvents;

public sealed record GetAccountSecurityEventsQuery(Guid UserId, int? Page, int? PageSize) : IQuery<Result<PagedResult<SecurityEventEntry>>>;

public sealed class GetAccountSecurityEventsQueryHandler(
    ISecurityEventQueries queries) : IQueryHandler<GetAccountSecurityEventsQuery, Result<PagedResult<SecurityEventEntry>>>
{
    public async ValueTask<Result<PagedResult<SecurityEventEntry>>> Handle(
        GetAccountSecurityEventsQuery query,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

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

        return await queries.GetForUserAsync(query.UserId, paging.Value, cancellationToken);
    }
}
