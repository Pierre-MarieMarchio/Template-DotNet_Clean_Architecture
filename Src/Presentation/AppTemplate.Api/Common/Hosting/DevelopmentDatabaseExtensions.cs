using AppTemplate.Infrastructure.Auth.Common.Contexts;
using AppTemplate.Infrastructure.Auth.Features.Auth.Seeding;
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
/// Two histories, one per context, applied one after the other. That leaves a window in which the
/// first schema exists and the second does not — and a lasting one if the second call fails, which
/// is why the failure is rethrown rather than logged and stepped over. Applying both from one
/// bootstrap is what keeps the window as short as a single process can make it; a deployment
/// applies each as its own explicit step, where the order is a deployment's to decide.
/// </para>
/// <para>
/// No hand-rolled retry loop: Npgsql's <c>EnableRetryOnFailure</c> handles transient unavailability.
/// Seeding is delegated to <see cref="IIdentitySeeder"/>, which is opt-in and refuses to run outside
/// Development.
/// </para>
/// </remarks>
internal static class DevelopmentDatabaseExtensions
{
    internal static async Task MigrateAndSeedForDevelopmentAsync(this WebApplication app)
    {
        ArgumentNullException.ThrowIfNull(app);

        await using var scope = app.Services.CreateAsyncScope();
        var services = scope.ServiceProvider;
        var logger = services.GetRequiredService<ILoggerFactory>().CreateLogger(typeof(DevelopmentDatabaseExtensions));

        try
        {
            logger.LogInformation("Applying migrations (Development only).");

            // Authentication first, because seeding an account writes to its tables.
            await services.GetRequiredService<AuthDbContext>().Database.MigrateAsync();
            await services.GetRequiredService<AppDbContext>().Database.MigrateAsync();

            await services.GetRequiredService<IIdentitySeeder>().SeedAsync();

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
