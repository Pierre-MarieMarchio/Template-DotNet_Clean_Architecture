using AppTemplate.PresentationApp.Common.Hosting;
//#if (externalAuth)
using AppTemplate.PresentationApp.Common.Sessions;
    //#endif
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace AppTemplate.PresentationApp.UnitTests.TestSupport;

internal static class PresentationHost
{
    public const string BaseAddress = "https://api.test/";
    //#if (localAuth)

    public static IHost Build(StubApi api, string baseAddress = BaseAddress) =>
        AppHost.Build(
            [$"--{ApiOptions.SectionName}:BaseAddress={baseAddress}"],
            builder => builder.Services.ConfigureHttpClientDefaults(http => http.ConfigurePrimaryHttpMessageHandler(() => api)));
    //#else

    public const string ClientId = "desktop-app";

    public static IHost Build(
        StubApi api,
        string baseAddress = BaseAddress,
        string authority = StubProvider.Authority,
        ISystemBrowser? browser = null) =>
        AppHost.Build(
            [
                $"--{ApiOptions.SectionName}:BaseAddress={baseAddress}",
                $"--{SignInOptions.SectionName}:Authority={authority}",
                $"--{SignInOptions.SectionName}:ClientId={ClientId}",
            ],
            builder =>
            {
                builder.Services.ConfigureHttpClientDefaults(http => http.ConfigurePrimaryHttpMessageHandler(() => api));
                builder.Services.AddSingleton(browser ?? new ScriptedBrowser());
            });
    //#endif
}
