using AppTemplate.Client.Common.Composition;
using AppTemplate.Client.Common.Errors;
using AppTemplate.Client.Common.Sessions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace AppTemplate.Client;

public static class ClientModule
{
    public const string HttpClientName = "AppTemplate.Client";

    //#if (localAuth)
    internal const string SessionHttpClientName = "AppTemplate.Client.Session";
    //#endif

    public static IHttpClientBuilder AddApiClient(this IServiceCollection services, Uri baseAddress)
    {
        ArgumentNullException.ThrowIfNull(baseAddress);

        return services.AddApiClient(_ => baseAddress);
    }

    public static IHttpClientBuilder AddApiClient(this IServiceCollection services, Func<IServiceProvider, Uri> baseAddress)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(baseAddress);

        //#if (localAuth)
        services.TryAddSingleton<IRefreshTokenStore, InMemoryRefreshTokenStore>();
        services.TryAddSingleton<ApiSession>();
        //#endif
        services.TryAddTransient<UnreadableAnswerHandler>();
        //#if (localAuth)
        services.TryAddTransient<ApiSessionHandler>();
        //#endif
        //#if (externalAuth)
        services.TryAddTransient<AccessTokenHandler>();
        //#endif
        services.TryAddTransient(provider =>
            ApiClientFactory.Create(provider.GetRequiredService<IHttpClientFactory>().CreateClient(HttpClientName)));

        //#if (localAuth)
        services.AddHttpClient(SessionHttpClientName, (provider, http) => http.BaseAddress = baseAddress(provider));

        //#endif
        var client = services
            .AddHttpClient(HttpClientName, (provider, http) => http.BaseAddress = baseAddress(provider))
            .AddHttpMessageHandler<UnreadableAnswerHandler>();
        //#if (localAuth)

        client.AddHttpMessageHandler<ApiSessionHandler>();
        //#endif
        //#if (externalAuth)

        client.AddHttpMessageHandler<AccessTokenHandler>();
        //#endif

        client.AddStandardResilienceHandler();

        return client;
    }
}
