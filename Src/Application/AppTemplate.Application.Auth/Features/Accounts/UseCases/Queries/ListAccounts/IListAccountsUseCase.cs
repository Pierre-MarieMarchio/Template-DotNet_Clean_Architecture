using AppTemplate.Application.Auth.Features.Accounts.Ports.AccountQueries;
using AppTemplate.Application.Core.Common.Results;
using AppTemplate.Application.Core.Common.UseCases;

namespace AppTemplate.Application.Auth.Features.Accounts.UseCases.Queries.ListAccounts;

public interface IListAccountsUseCase : IUseCase<ListAccountsQuery, Result<PagedResult<AccountSummary>>>;
