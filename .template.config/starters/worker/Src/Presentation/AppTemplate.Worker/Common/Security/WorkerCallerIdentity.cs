using AppTemplate.Application.Core.Common.Ports;
using AppTemplate.Infrastructure.Core.Common.Saving.Auditing;
using AppTemplate.Presentation.Core.Common.Jobs;
using AppTemplate.Presentation.Core;

namespace AppTemplate.Worker.Common.Security;

internal static class WorkerCallerIdentity
{
    internal static IServiceCollection AddWorkerCallerIdentity(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddBackgroundScope();

        services.AddNoCallerIdentity();
        services.AddScoped<IAuditActor, BackgroundAuditActor>();
        services.AddScoped<ICurrentAccessToken, NoCurrentAccessToken>();

        return services;
    }
}
