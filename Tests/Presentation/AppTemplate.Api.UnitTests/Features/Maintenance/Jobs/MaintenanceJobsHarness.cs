using AppTemplate.Api.Common.Jobs;
//#if (localAuth)
using AppTemplate.Api.Features.Auth.Jobs;
//#endif
using AppTemplate.Api.Features.Maintenance.Jobs;
//#if (localAuth)
using AppTemplate.Application.Auth.Features.Auth.UseCases.Commands.PurgeExpiredRefreshTokens;
//#endif
using AppTemplate.Application.Core.Features.Maintenance.UseCases.Commands.PurgeExpiredIdempotencyKeys;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace AppTemplate.Api.UnitTests.Features.Maintenance.Jobs;

internal static class MaintenanceJobsHarness
{
    /// <summary>
    /// The real registration — the idempotency purge from the maintenance feature and, with the
    /// supplied accounts, the refresh-token purge from authentication's — over fake use cases, with
    /// the option objects replaced so the interval can be shorter than the validator allows a
    /// deployment to choose.
    /// </summary>
    internal static MaintenanceBackgroundService Create(
        IPurgeExpiredIdempotencyKeysUseCase idempotency,
        //#if (localAuth)
        IPurgeExpiredRefreshTokensUseCase refreshTokens,
        //#endif
        MaintenanceWorkerOptions options,
        //#if (localAuth)
        bool refreshTokensEnabled = true,
        //#endif
        ILogger<MaintenanceBackgroundService>? logger = null)
    {
        var configuration = new ConfigurationBuilder().Build();

        var services = new ServiceCollection();
        services.AddBackgroundScope();
        services.AddScoped(_ => idempotency);
        //#if (localAuth)
        services.AddScoped(_ => refreshTokens);
        //#endif
        services.AddMaintenanceJobs(configuration);
        //#if (localAuth)
        services.AddAuthMaintenanceJobs(configuration);
        //#endif
        services.AddSingleton(Options.Create(options));
        //#if (localAuth)
        services.AddSingleton(Options.Create(new RefreshTokenPurgeOptions
        {
            PurgeExpiredRefreshTokensEnabled = refreshTokensEnabled,
        }));
        //#endif
        services.AddSingleton(logger ?? NullLogger<MaintenanceBackgroundService>.Instance);

        var provider = services.BuildServiceProvider();

        return provider.GetServices<IHostedService>().OfType<MaintenanceBackgroundService>().Single();
    }

}
