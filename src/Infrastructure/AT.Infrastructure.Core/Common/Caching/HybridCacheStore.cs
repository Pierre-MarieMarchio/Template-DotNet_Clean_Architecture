using AppTemplate.Application.Core.Common.Ports;
using Microsoft.Extensions.Caching.Hybrid;

namespace AppTemplate.Infrastructure.Core.Common.Caching;

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
