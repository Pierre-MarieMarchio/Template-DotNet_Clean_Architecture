using AppTemplate.PresentationApp.Common.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace AppTemplate.PresentationApp.UnitTests.TestSupport;

internal static class PresentationHost
{
    public const string BaseAddress = "https://api.test/";

    public static IHost Build(StubApi api, string baseAddress = BaseAddress) =>
        AppHost.Build(
            [$"--{ApiOptions.SectionName}:BaseAddress={baseAddress}"],
            builder => builder.Services.ConfigureHttpClientDefaults(http => http.ConfigurePrimaryHttpMessageHandler(() => api)));
}
