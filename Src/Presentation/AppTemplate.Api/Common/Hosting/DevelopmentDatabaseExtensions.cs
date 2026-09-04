using AppTemplate.Infrastructure.Persistence.Common.Contexts;
using AppTemplate.Infrastructure.Persistence.Features.Identity.Seeding;
using Microsoft.EntityFrameworkCore;

namespace AppTemplate.Api.Common.Hosting;

/// <summary>
/// Development-only schema bootstrap.
/// </summary>
/// <remarks>
/// The version this replaces ran on every start in every environment, before Kestrel began
/// listening: two sequential retry loops of up to twenty seconds each, with no try/catch, so a bad
/// connection string produced a forty-second silent hang and then an unhandled exception. It also
/// seeded an <c>admin</c>/<c>admin</c> account outside its own environment guard.
/// <para>
/// Here it runs after the app is built but only under Development, failures are logged with context
/// before rethrowing, and seeding is delegated to <see cref="IIdentitySeeder"/>, which is itself
/// opt-in and refuses to run outside Development. Npgsql's <c>EnableRetryOnFailure</c> handles
/// transient unavailability, so no hand-rolled retry loop is needed.
/// </para>
/// <para>
/// It applies <em>one</em> migration history, against the single <see cref="AppDbContext"/>. The
/// previous version migrated two contexts in sequence, which meant a window — and, if the second
/// call failed, a lasting state — in which one module's schema existed and the other's did not.
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
