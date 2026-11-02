using AppTemplate.Application.Auth.Features.Accounts.Model.Policies;
using AppTemplate.Application.Auth.Features.Accounts.Ports.AccountQueries;
using AppTemplate.Application.Core.Common.Collections;
using AppTemplate.Application.Core.Common.Results;
using AppTemplate.Application.Core.Common.Validation;
using FluentValidation;

namespace AppTemplate.Application.Auth.Features.Accounts.UseCases.Queries.ListAccounts;

public sealed class ListAccountsUseCase(
    IAccountQueries queries,
    IValidator<ListAccountsQuery> validator) : IListAccountsUseCase
{
    public async Task<Result<PagedResult<AccountSummary>>> ExecuteAsync(
        ListAccountsQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        var validation = await validator.EnsureValidAsync(query, cancellationToken);

        if (validation.IsFailure)
        {
            return validation.To<PagedResult<AccountSummary>>();
        }

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
