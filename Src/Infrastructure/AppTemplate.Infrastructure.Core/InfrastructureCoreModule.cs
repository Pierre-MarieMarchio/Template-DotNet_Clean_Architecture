using AppTemplate.Application.Core.Common.Ports;
using AppTemplate.Infrastructure.Core.Common.Caching;
using Microsoft.Extensions.DependencyInjection;

namespace AppTemplate.Infrastructure.Core;

/// <summary>What this project registers, and every host that reads through a cache calls it.</summary>
public static class InfrastructureCoreModule
{
    /// <summary>
    /// Registers <see cref="ICacheStore"/> over <c>HybridCache</c>, in process. A deployment that
    /// wants a shared second level registers an <c>IDistributedCache</c> beside this call.
    /// </summary>
    public static IServiceCollection AddCacheStore(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddHybridCache();
        services.AddSingleton<ICacheStore, HybridCacheStore>();

        return services;
    }
}
