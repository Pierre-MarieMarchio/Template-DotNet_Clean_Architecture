using AppTemplate.Client.Common.Sessions;
using AppTemplate.Client.Generated;
using Microsoft.Extensions.DependencyInjection;

namespace AppTemplate.Client.UnitTests.TestSupport;

internal sealed class ClientHarness : IDisposable
{
    public static readonly Uri BaseAddress = new("https://api.test/");

    private readonly ServiceProvider _provider;

    public ClientHarness(StubApi api, IRefreshTokenStore? refreshTokens = null)
    {
        Api = api;

        var services = new ServiceCollection();

        if (refreshTokens is not null)
        {
            services.AddSingleton(refreshTokens);
        }

        services.ConfigureHttpClientDefaults(http => http.ConfigurePrimaryHttpMessageHandler(() => api));
        services.AddApiClient(BaseAddress);

        _provider = services.BuildServiceProvider();
    }

    public StubApi Api { get; }

    public ApiClient Client => _provider.GetRequiredService<ApiClient>();

    public ApiSession Session => _provider.GetRequiredService<ApiSession>();

    public IRefreshTokenStore RefreshTokens => _provider.GetRequiredService<IRefreshTokenStore>();

    public HttpClient Http => _provider.GetRequiredService<IHttpClientFactory>().CreateClient(ClientModule.HttpClientName);

    public void Dispose() => _provider.Dispose();
}
