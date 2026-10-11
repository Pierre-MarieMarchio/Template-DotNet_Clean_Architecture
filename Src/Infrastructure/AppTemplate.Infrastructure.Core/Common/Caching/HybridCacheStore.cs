using AppTemplate.Application.Core.Common.Ports;
using Microsoft.Extensions.Caching.Hybrid;

namespace AppTemplate.Infrastructure.Core.Common.Caching;

/// <summary><see cref="ICacheStore"/> over <see cref="HybridCache"/>.</summary>
/// <remarks>With no distributed cache registered, <see cref="HybridCache"/> is in-process: each
/// process holds its own copy and no eviction reaches the others; a second level is added by
/// registering an <c>IDistributedCache</c>, with nothing here changing. Concurrent misses on one key
/// run the factory once and share its result. Both expirations are the same lifetime — the local
/// copy outliving the shared one would hand back a value the rest of the deployment has already
/// dropped.</remarks>
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
