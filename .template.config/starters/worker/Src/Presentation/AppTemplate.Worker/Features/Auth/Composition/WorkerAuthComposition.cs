using AppTemplate.Application.Auth;
using AppTemplate.Application.Auth.Features.Sessions.Ports.CurrentSession;
//#if (anyFeature)
using AppTemplate.Application.Core.Common.Ports;
//#endif
using AppTemplate.Infrastructure.Auth;
using AppTemplate.Worker.Features.Auth.Jobs;

namespace AppTemplate.Worker.Features.Auth.Composition;

internal static class WorkerAuthComposition
{
    internal static IServiceCollection AddWorkerAuthentication(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        services.AddAuthApplication();

        services.AddAuthModule(configuration);
        services.AddScoped<ICurrentSession, NoCallerCurrentSession>();
        services.AddAuthMaintenanceJobs(configuration);
        //#if (anyFeature)

        services.AddScoped<IUserAccountDirectory, AccountUserAccountDirectory>();
        //#endif

        return services;
    }
}
