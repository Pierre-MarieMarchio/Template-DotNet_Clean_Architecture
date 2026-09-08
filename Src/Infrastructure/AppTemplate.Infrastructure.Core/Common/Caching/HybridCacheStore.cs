using AppTemplate.Application.Core.Common.Ports;
using Microsoft.Extensions.Caching.Hybrid;

namespace AppTemplate.Infrastructure.Core.Common.Caching;

/// <summary>
/// <see cref="ICacheStore"/> over <see cref="HybridCache"/>.
/// </summary>
/// <remarks>
/// <para>
/// With no distributed cache registered, <see cref="HybridCache"/> is an in-process cache, so each
/// process holds its own copy and no eviction reaches the others. A second level is added by
/// registering an <c>IDistributedCache</c>; nothing here changes, and no caller does either.
/// </para>
/// <para>
/// Concurrent misses on one key run the factory once and share its result, which is what a plain
/// dictionary would have to be written to do.
/// </para>
/// <para>
/// Both expirations are set to the same lifetime: the local copy outliving the shared one would
/// hand back a value the rest of the deployment has already dropped.
/// </para>
/// </remarks>
internal sealed class HybridCacheStore(HybridCache cache) : ICacheStore
{
    public Task<T> GetOrCreateAsync<T>(
        string key,
        Func<CancellationToken, Task<T>> factory,
        TimeSpan lifetime,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(factory);

        var options = new HybridCacheEntryOptions
        {
            Expiration = lifetime,
            LocalCacheExpiration = lifetime,
        };

        // The factory is passed as the state so the callback can stay static: a closure here would
        // allocate on every hit as well as every miss.
        return cache.GetOrCreateAsync(
            key,
            factory,
            static async (state, token) => await state(token).ConfigureAwait(false),
            options,
            cancellationToken: cancellationToken).AsTask();
    }

    public Task RemoveAsync(string key, CancellationToken cancellationToken = default) =>
        cache.RemoveAsync(key, cancellationToken).AsTask();
}
