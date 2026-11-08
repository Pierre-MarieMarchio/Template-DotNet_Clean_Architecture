using AppTemplate.Application.Core.Common.Collections;
using AppTemplate.Application.Core.Common.Results;

namespace AppTemplate.Application.Auth.Features.Accounts.Ports.AccountQueries;

public interface IAccountQueries
{
    Task<PagedResult<AccountSummary>> ListAsync(
        string? emailPrefix,
        PageRequest paging,
        CancellationToken cancellationToken = default);
}
