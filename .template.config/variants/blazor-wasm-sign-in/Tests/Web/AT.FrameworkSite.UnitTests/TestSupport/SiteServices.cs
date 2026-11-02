using AppTemplate.Client;
using AppTemplate.FrameworkSite.Common.Hosting;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.WebAssembly.Authentication;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace AppTemplate.FrameworkSite.UnitTests.TestSupport;

internal static class SiteServices
{
    public const string Authority = "https://login.example.com/realms/acme";

    public const string ClientId = "browser-app";

    public static ServiceProvider Build(
        StubApi api,
        StubAccessTokenProvider? tokens = null,
        IDictionary<string, string?>? settings = null)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(settings ?? new Dictionary<string, string?>
            {
                [$"{SignInOptions.SectionName}:Authority"] = Authority,
                [$"{SignInOptions.SectionName}:ClientId"] = ClientId,
            })
            .Build();
        var services = new ServiceCollection();

        services.AddSingleton<NavigationManager, AppNavigationManager>();
        services.AddSignIn(configuration);
        services.AddApiClient(new Uri(AppNavigationManager.BaseAddress))
            .ConfigurePrimaryHttpMessageHandler(() => api);

        if (tokens is not null)
        {
            services.AddScoped<IAccessTokenProvider>(_ => tokens);
        }

        return services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
    }
}
