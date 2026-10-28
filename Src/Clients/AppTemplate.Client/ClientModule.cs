using AppTemplate.Client.Common.Composition;
using AppTemplate.Client.Common.Errors;
using AppTemplate.Client.Common.Sessions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace AppTemplate.Client;

public static class ClientModule
{
    public const string HttpClientName = "AppTemplate.Client";

    internal const string SessionHttpClientName = "AppTemplate.Client.Session";

    public static IHttpClientBuilder AddApiClient(this IServiceCollection services, Uri baseAddress)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(baseAddress);

        services.TryAddSingleton<IRefreshTokenStore, InMemoryRefreshTokenStore>();
        services.TryAddSingleton<ApiSession>();
        services.TryAddTransient<UnreadableAnswerHandler>();
        services.TryAddTransient<ApiSessionHandler>();
        services.TryAddTransient(provider =>
            ApiClientFactory.Create(provider.GetRequiredService<IHttpClientFactory>().CreateClient(HttpClientName)));

        services.AddHttpClient(SessionHttpClientName, http => http.BaseAddress = baseAddress);

        var client = services
            .AddHttpClient(HttpClientName, http => http.BaseAddress = baseAddress)
            .AddHttpMessageHandler<UnreadableAnswerHandler>()
            .AddHttpMessageHandler<ApiSessionHandler>();

        client.AddStandardResilienceHandler();

        return client;
    }
}
