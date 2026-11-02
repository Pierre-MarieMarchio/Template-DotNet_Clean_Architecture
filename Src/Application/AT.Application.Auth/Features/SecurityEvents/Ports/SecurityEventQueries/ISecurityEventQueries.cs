using AppTemplate.Application.Core.Common.Collections;
using AppTemplate.Application.Core.Common.Results;

namespace AppTemplate.Application.Auth.Features.SecurityEvents.Ports.SecurityEventQueries;

public interface ISecurityEventQueries
{
    Task<PagedResult<SecurityEventEntry>> GetForUserAsync(
        Guid userId,
        PageRequest paging,
        CancellationToken cancellationToken = default);
}
