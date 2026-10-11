using AppTemplate.Application.Core.Common.Collections;
using AppTemplate.Application.Core.Common.Results;

namespace AppTemplate.Application.Auth.Features.Auth.Ports.SecurityEventQueries;

/// <summary>
/// Reads the trail <see cref="SecurityEventLog.ISecurityEventLog"/> keeps, one account at a time.
/// <para>
/// There is deliberately no read across accounts: the two callers are the account holder, reading
/// their own, and an administrator, reading the one account they named. Whoever the caller is, the
/// account id is the filter, so nothing here can answer with another user's rows.
/// </para>
/// </summary>
public interface ISecurityEventQueries
{
    /// <summary>
    /// One page of <paramref name="userId"/>'s events, newest first. An id that names no account —
    /// or names one since deleted — answers whatever is still retained for it, which may be nothing.
    /// </summary>
    Task<PagedResult<SecurityEventEntry>> GetForUserAsync(
        Guid userId,
        PageRequest paging,
        CancellationToken cancellationToken = default);
}
