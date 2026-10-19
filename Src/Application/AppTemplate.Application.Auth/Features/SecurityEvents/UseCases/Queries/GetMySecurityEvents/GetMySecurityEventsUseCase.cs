using AppTemplate.Application.Auth.Features.SecurityEvents.Model.Policies;
using AppTemplate.Application.Auth.Features.SecurityEvents.Ports.SecurityEventQueries;
using AppTemplate.Application.Core.Common.Collections;
using AppTemplate.Application.Core.Common.Ports;
using AppTemplate.Application.Core.Common.Results;

namespace AppTemplate.Application.Auth.Features.SecurityEvents.UseCases.Queries.GetMySecurityEvents;

/// <summary>
/// Reads the caller's own trail: the account id is the caller's, never an input, so the answer can
/// only ever be the caller's rows.
/// </summary>
public sealed class GetMySecurityEventsUseCase(
    ISecurityEventQueries queries,
    ICurrentUser currentUser) : IGetMySecurityEventsUseCase
{
    /// <inheritdoc />
    public async Task<Result<PagedResult<SecurityEventEntry>>> ExecuteAsync(
        GetMySecurityEventsQuery query,
        CancellationToken cancellationToken = default)
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
