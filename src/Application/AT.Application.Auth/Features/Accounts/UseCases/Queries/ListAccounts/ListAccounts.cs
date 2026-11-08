using AppTemplate.Application.Auth.Features.Accounts.Model.Policies;
using AppTemplate.Application.Auth.Features.Accounts.Ports.AccountQueries;
using AppTemplate.Application.Core.Common.Collections;
using AppTemplate.Application.Core.Common.Results;
using Mediator;

namespace AppTemplate.Application.Auth.Features.Accounts.UseCases.Queries.ListAccounts;

public sealed record ListAccountsQuery(string? Email, int? Page, int? PageSize) : IQuery<Result<PagedResult<AccountSummary>>>;

public sealed class ListAccountsQueryHandler(
    IAccountQueries queries) : IQueryHandler<ListAccountsQuery, Result<PagedResult<AccountSummary>>>
{
    public async ValueTask<Result<PagedResult<AccountSummary>>> Handle(
        ListAccountsQuery query,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        var paging = PageRequest.Create(
            PagingMode.Offset,
            query.Page,
            query.PageSize,
            cursor: null,
            AccountCollectionPolicy.Instance);

        if (paging.IsFailure)
        {
            return paging.To<PagedResult<AccountSummary>>();
        }

        string? emailPrefix = string.IsNullOrWhiteSpace(query.Email) ? null : query.Email.Trim();

        return await queries.ListAsync(emailPrefix, paging.Value, cancellationToken);
    }
}
