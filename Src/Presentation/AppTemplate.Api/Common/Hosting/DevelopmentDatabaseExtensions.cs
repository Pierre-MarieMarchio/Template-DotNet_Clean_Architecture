using AppTemplate.Infrastructure.Persistence.Common.Contexts;
using AppTemplate.Infrastructure.Persistence.Features.Identity.Seeding;
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
/// One migration history, against the single <see cref="AppDbContext"/>: a second context would
/// mean a window in which one module's schema exists and the other's does not, and a lasting one
/// if the second call failed.
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
