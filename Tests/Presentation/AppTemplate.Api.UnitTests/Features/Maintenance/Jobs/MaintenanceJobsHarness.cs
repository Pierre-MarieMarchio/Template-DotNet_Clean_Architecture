using AppTemplate.Api.Common.Jobs;
using AppTemplate.Api.Features.Auth.Jobs;
using AppTemplate.Api.Features.Maintenance.Jobs;
using AppTemplate.Application.Auth.Features.Auth.UseCases.Commands.PurgeExpiredRefreshTokens;
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
    /// The real registration — the idempotency purge from the maintenance feature, the refresh-token
    /// purge from authentication's — over fake use cases, with the two option objects replaced so the
    /// interval can be shorter than the validator allows a deployment to choose.
    /// </summary>
    internal static MaintenanceBackgroundService Create(
        IPurgeExpiredIdempotencyKeysUseCase idempotency,
        IPurgeExpiredRefreshTokensUseCase refreshTokens,
        MaintenanceWorkerOptions options,
        bool refreshTokensEnabled = true,
        ILogger<MaintenanceBackgroundService>? logger = null)
    {
        var configuration = new ConfigurationBuilder().Build();

        var services = new ServiceCollection();
        services.AddBackgroundScope();
        services.AddScoped(_ => idempotency);
        services.AddScoped(_ => refreshTokens);
        services.AddMaintenanceJobs(configuration);
        services.AddAuthMaintenanceJobs(configuration);
        services.AddSingleton(Options.Create(options));
        services.AddSingleton(Options.Create(new RefreshTokenPurgeOptions
        {
            PurgeExpiredRefreshTokensEnabled = refreshTokensEnabled,
        }));
        services.AddSingleton(logger ?? NullLogger<MaintenanceBackgroundService>.Instance);

        var provider = services.BuildServiceProvider();

        return provider.GetServices<IHostedService>().OfType<MaintenanceBackgroundService>().Single();
    }

}
