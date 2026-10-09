using AppTemplate.Infrastructure.Persistence.Common.Contexts;
using Microsoft.EntityFrameworkCore;

namespace AppTemplate.Api.Common.Hosting;

/// <summary>
/// Development-only schema bootstrap.
/// </summary>
/// <remarks>
/// Runs after the app is built and only under Development, so a deployment never migrates from the
/// process that serves requests: that needs DDL rights at runtime and races between replicas on
/// <c>__EFMigrationsHistory</c>. Failures are logged with context before rethrowing.
/// <para>
/// One history per context, applied one after the other: the business schema first, then every
/// <see cref="DevelopmentDatabaseStep"/> a module registered, in registration order. A failure part
/// way leaves the earlier schemas in place and the later ones absent, which is why it is rethrown
/// rather than stepped over. Transient unavailability is Npgsql's <c>EnableRetryOnFailure</c> and
/// not a loop here.
/// </para>
/// </remarks>
internal static class DevelopmentDatabaseExtensions
{
    /// <summary>Adds a module's step to the development bootstrap.</summary>
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
            // Rethrown: a development environment that cannot reach its database should fail loudly
            // and immediately, not start and then fail one request at a time.
            logger.LogCritical(exception, "Development database bootstrap failed.");
            throw;
        }
    }
}
