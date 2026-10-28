//#if (localAuth)
using AppTemplate.Client.Common.Sessions;
//#endif
using AppTemplate.Client.Generated;
using Microsoft.Extensions.DependencyInjection;

namespace AppTemplate.Client.UnitTests.TestSupport;

internal sealed class ClientHarness : IDisposable
{
    public static readonly Uri BaseAddress = new("https://api.test/");

    private readonly ServiceProvider _provider;

    /*#if (localAuth)*/
    public ClientHarness(StubApi api, IRefreshTokenStore? refreshTokens = null)
    /*#else
    public ClientHarness(StubApi api)
    #endif*/
    {
        Api = api;

        var services = new ServiceCollection();

        //#if (localAuth)
        if (refreshTokens is not null)
        {
            services.AddSingleton(refreshTokens);
        }

        //#endif
        services.ConfigureHttpClientDefaults(http => http.ConfigurePrimaryHttpMessageHandler(() => api));
        services.AddApiClient(BaseAddress);

        _provider = services.BuildServiceProvider();
    }

    public StubApi Api { get; }

    public ApiClient Client => _provider.GetRequiredService<ApiClient>();

    //#if (localAuth)
    public ApiSession Session => _provider.GetRequiredService<ApiSession>();

    public IRefreshTokenStore RefreshTokens => _provider.GetRequiredService<IRefreshTokenStore>();

    //#endif
    public HttpClient Http => _provider.GetRequiredService<IHttpClientFactory>().CreateClient(ClientModule.HttpClientName);

    public void Dispose() => _provider.Dispose();
}
