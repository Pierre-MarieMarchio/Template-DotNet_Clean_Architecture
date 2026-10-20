using AppTemplate.Application.Auth.Features.SecurityEvents.Ports.SecurityEventQueries;
using AppTemplate.Application.Core.Common.Collections;
using AppTemplate.Application.Core.Common.Results;
using AppTemplate.Infrastructure.Auth.Common.Contexts;
using Microsoft.EntityFrameworkCore;

namespace AppTemplate.Infrastructure.Auth.Features.SecurityEvents.Access.Queries;

internal sealed class SecurityEventQueries(AuthDbContext context) : ISecurityEventQueries
{
    public async Task<PagedResult<SecurityEventEntry>> GetForUserAsync(
        Guid userId,
        PageRequest paging,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(paging);

        var owned = context.SecurityEvents.AsNoTracking().Where(securityEvent => securityEvent.UserId == userId);

        int totalCount = await owned.CountAsync(cancellationToken);
        int page = paging.Page ?? 1;

        var items = await owned
            .OrderByDescending(securityEvent => securityEvent.OccurredAt)
            .ThenByDescending(securityEvent => securityEvent.Id)
            .Skip((page - 1) * paging.PageSize)
            .Take(paging.PageSize)
            .Select(securityEvent => new SecurityEventEntry(
                securityEvent.Id,
                securityEvent.Kind,
                securityEvent.OccurredAt,
                securityEvent.IpAddress,
                securityEvent.UserAgent,
                securityEvent.Detail))
            .ToListAsync(cancellationToken);

        return PagedResult.Offset(items, page, paging.PageSize, totalCount);
    }
}
