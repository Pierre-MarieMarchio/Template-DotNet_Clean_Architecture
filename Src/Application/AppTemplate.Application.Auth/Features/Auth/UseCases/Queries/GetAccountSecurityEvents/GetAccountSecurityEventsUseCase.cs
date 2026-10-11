using AppTemplate.Application.Auth.Features.Auth.Policies;
using AppTemplate.Application.Auth.Features.Auth.Ports.SecurityEventQueries;
using AppTemplate.Application.Core.Common.Collections;
using AppTemplate.Application.Core.Common.Results;
using AppTemplate.Application.Core.Common.Validation;
using FluentValidation;

namespace AppTemplate.Application.Auth.Features.Auth.UseCases.Queries.GetAccountSecurityEvents;

/// <summary>
/// Reads one account's trail on an administrator's authority, which the transport enforces before
/// this runs.
/// <para>
/// <b>No 404 for an account that does not exist.</b> The trail is retained after an account is
/// deleted — that is half of why it is kept — so "no such account" is exactly the case an
/// investigation needs answered with the rows that remain. An id naming nothing answers an empty page.
/// </para>
/// </summary>
public sealed class GetAccountSecurityEventsUseCase(
    ISecurityEventQueries queries,
    IValidator<GetAccountSecurityEventsQuery> validator) : IGetAccountSecurityEventsUseCase
{
    /// <inheritdoc />
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
