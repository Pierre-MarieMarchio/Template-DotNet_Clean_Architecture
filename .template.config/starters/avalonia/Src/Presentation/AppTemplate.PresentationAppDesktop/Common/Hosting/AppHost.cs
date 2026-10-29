using AppTemplate.Client;
//#if (externalAuth)
using AppTemplate.Client.Common.Sessions;
using AppTemplate.PresentationAppDesktop.Common.Sessions;
//#endif
using AppTemplate.PresentationAppDesktop.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace AppTemplate.PresentationAppDesktop.Common.Hosting;

internal static class AppHost
{
    public static IHost Build(string[] args, Action<HostApplicationBuilder>? configure = null)
    {
        var builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings
        {
            Args = args,
            ContentRootPath = AppContext.BaseDirectory,
        });

        builder.Services.AddSingleton<IValidateOptions<ApiOptions>, ApiOptionsValidator>();
        builder.Services.AddOptions<ApiOptions>()
            .Bind(builder.Configuration.GetSection(ApiOptions.SectionName))
            .ValidateOnStart();

        //#if (externalAuth)
        builder.Services.AddSingleton<IValidateOptions<SignInOptions>, SignInOptionsValidator>();
        builder.Services.AddOptions<SignInOptions>()
            .Bind(builder.Configuration.GetSection(SignInOptions.SectionName))
            .ValidateOnStart();

        builder.Services.AddHttpClient(DesktopSession.HttpClientName);
        builder.Services.AddSingleton<ISystemBrowser, SystemBrowser>();
        builder.Services.AddSingleton<DesktopSession>();
        builder.Services.AddSingleton<IAccessTokenSource>(provider => provider.GetRequiredService<DesktopSession>());

        //#endif
        builder.Services.AddApiClient(provider => provider.GetRequiredService<IOptions<ApiOptions>>().Value.BaseAddress!);
        builder.Services.AddTransient<MainViewModel>();

        configure?.Invoke(builder);

        return builder.Build();
    }
}
