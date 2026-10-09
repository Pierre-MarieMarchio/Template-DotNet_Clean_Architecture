using AppTemplate.Domain.Core.Common.Primitives;
using AppTemplate.Domain.Features.Files.ValueObjects;

namespace AppTemplate.Application.Features.Files.Ports.OwnerQuotaLock;

/// <summary>
/// Serialises the registrations of one owner in one category, across every host sharing the store,
/// so that a quota read and the insert it allows cannot interleave with another registration's.
/// </summary>
public interface IOwnerQuotaLock
{
    /// <summary>
    /// Waits for the (<paramref name="owner"/>, <paramref name="category"/>) lock, runs
    /// <paramref name="work"/> while holding it, and releases it however the work ends.
    /// </summary>
    /// <remarks>
    /// The work must commit before it returns: the lock is released afterwards, and the next holder
    /// reads only what was committed.
    /// </remarks>
    /// <exception cref="OwnerQuotaLockTimeoutException">The lock was not obtained within the
    /// adapter's wait limit; <paramref name="work"/> did not run.</exception>
    Task<T> RunExclusivelyAsync<T>(
        UserId owner,
        FileCategory category,
        Func<CancellationToken, Task<T>> work,
        CancellationToken cancellationToken = default);
}
