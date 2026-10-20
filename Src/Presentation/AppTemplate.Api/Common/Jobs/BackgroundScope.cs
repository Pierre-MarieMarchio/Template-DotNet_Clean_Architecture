using Microsoft.Extensions.DependencyInjection.Extensions;

namespace AppTemplate.Api.Common.Jobs;

internal sealed class BackgroundScope
{
    public bool IsActive { get; private set; }

    internal void Enter() => IsActive = true;
}

internal static class BackgroundScopeExtensions
{
    internal static IServiceCollection AddBackgroundScope(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.TryAddScoped<BackgroundScope>();

        return services;
    }

    internal static AsyncServiceScope CreateBackgroundScope(this IServiceScopeFactory scopeFactory)
    {
        ArgumentNullException.ThrowIfNull(scopeFactory);

        AsyncServiceScope scope = scopeFactory.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<BackgroundScope>().Enter();

        return scope;
    }
}
