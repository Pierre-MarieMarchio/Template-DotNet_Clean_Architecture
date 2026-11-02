using AppTemplate.Application.Auth.Features.SecurityEvents.Model.Policies;
using AppTemplate.Application.Auth.Features.SecurityEvents.Ports.SecurityEventQueries;
using AppTemplate.Application.Core.Common.Collections;
using AppTemplate.Application.Core.Common.Results;
using AppTemplate.Application.Core.Common.Validation;
using FluentValidation;

namespace AppTemplate.Application.Auth.Features.SecurityEvents.UseCases.Queries.GetAccountSecurityEvents;

public sealed class GetAccountSecurityEventsUseCase(
    ISecurityEventQueries queries,
    IValidator<GetAccountSecurityEventsQuery> validator) : IGetAccountSecurityEventsUseCase
{
    public async Task<Result<PagedResult<SecurityEventEntry>>> ExecuteAsync(
        GetAccountSecurityEventsQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        var validation = await validator.EnsureValidAsync(query, cancellationToken);

        if (validation.IsFailure)
        {
            return validation.To<PagedResult<SecurityEventEntry>>();
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

        return await queries.GetForUserAsync(query.UserId, paging.Value, cancellationToken);
    }
}
