using AppTemplate.Infrastructure.Persistence.Common.Contexts;
using Microsoft.EntityFrameworkCore;

namespace AppTemplate.Api.Common.Hosting;

internal static class DevelopmentDatabaseExtensions
{
    internal static IServiceCollection AddDevelopmentDatabaseStep(
        this IServiceCollection services,
        string name,
        Func<IServiceProvider, CancellationToken, Task> runAsync)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(runAsync);

        return services.AddSingleton(new DevelopmentDatabaseStep(name, runAsync));
    }

    internal static async Task MigrateAndSeedForDevelopmentAsync(this WebApplication app)
    {
        ArgumentNullException.ThrowIfNull(app);

        await using var scope = app.Services.CreateAsyncScope();
        var services = scope.ServiceProvider;
        var logger = services.GetRequiredService<ILoggerFactory>().CreateLogger(typeof(DevelopmentDatabaseExtensions));

        try
        {
            logger.LogInformation("Applying migrations (Development only).");

            await services.GetRequiredService<AppDbContext>().Database.MigrateAsync();

            foreach (var step in services.GetServices<DevelopmentDatabaseStep>())
            {
                if (logger.IsEnabled(LogLevel.Information))
                {
                    logger.LogInformation("Running the '{Step}' development database step.", step.Name);
                }

                await step.RunAsync(services, app.Lifetime.ApplicationStopping);
            }

            logger.LogInformation("Database is ready.");
        }
        catch (Exception exception)
        {
            logger.LogCritical(exception, "Development database bootstrap failed.");
            throw;
        }
    }
}
