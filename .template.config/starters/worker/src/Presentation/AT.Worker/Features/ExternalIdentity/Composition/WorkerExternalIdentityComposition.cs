using AppTemplate.Application;
//#if (anyFeature)
using AppTemplate.Application.Core.Common.Ports;
//#endif
using AppTemplate.Infrastructure.Persistence;

namespace AppTemplate.Worker.Features.ExternalIdentity.Composition;

internal static class WorkerExternalIdentityComposition
{
    internal static IServiceCollection AddWorkerExternalIdentity(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        services.AddExternalIdentity();
        services.AddExternalIdentityPersistence(configuration);
        //#if (anyFeature)

        services.AddScoped<IUserAccountDirectory, ExternalIdentityUserAccountDirectory>();
        //#endif

        return services;
    }
}
