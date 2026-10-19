using AppTemplate.Domain.Core.Common.Primitives;
using AppTemplate.Domain.Features.Files.Model.ValueObjects;

namespace AppTemplate.Application.Features.Files.Ports.OwnerQuotaLock;

/// <summary>
/// Serialises one owner's registrations in one category across every host sharing the store, so a quota read and the
/// insert it allows cannot interleave with another's.
/// </summary>
public interface IOwnerQuotaLock
{
    /// <summary>
    /// Waits for the (<paramref name="owner"/>, <paramref name="category"/>) lock, runs <paramref name="work"/> under
    /// it and releases it however the work ends. The work must commit before it returns: the next holder reads only
    /// committed rows.
    /// </summary>
    /// <exception cref="OwnerQuotaLockTimeoutException">The lock was not obtained within the adapter's wait limit; the work did not run.</exception>
    Task<T> RunExclusivelyAsync<T>(
        UserId owner,
        FileCategory category,
        Func<CancellationToken, Task<T>> work,
        CancellationToken cancellationToken = default);
}
