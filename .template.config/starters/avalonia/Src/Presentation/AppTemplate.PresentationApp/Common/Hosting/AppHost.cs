using AppTemplate.Client;
using AppTemplate.PresentationApp.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace AppTemplate.PresentationApp.Common.Hosting;

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

        builder.Services.AddApiClient(provider => provider.GetRequiredService<IOptions<ApiOptions>>().Value.BaseAddress!);
        builder.Services.AddTransient<MainViewModel>();

        configure?.Invoke(builder);

        return builder.Build();
    }
}
