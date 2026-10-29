using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace AppTemplate.Presentation.Core.Common.Jobs;

public static class BackgroundScopeExtensions
{
    public static IServiceCollection AddBackgroundScope(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.TryAddScoped<BackgroundScope>();

        return services;
    }

    public static AsyncServiceScope CreateBackgroundScope(this IServiceScopeFactory scopeFactory)
    {
        ArgumentNullException.ThrowIfNull(scopeFactory);

        AsyncServiceScope scope = scopeFactory.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<BackgroundScope>().Enter();

        return scope;
    }
}
