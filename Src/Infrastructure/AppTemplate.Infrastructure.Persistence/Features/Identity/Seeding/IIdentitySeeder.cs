namespace AppTemplate.Infrastructure.Persistence.Features.Identity.Seeding;

/// <summary>
/// Puts the rows a fresh database needs in place: the <c>Admin</c> role, and — in Development only — one
/// administrator account.
/// <para>
/// Public because the host calls it: <c>MigrateAndSeedForDevelopmentAsync</c> resolves this after applying
/// migrations. The implementation is internal, so the only thing a caller can name is the operation.
/// </para>
/// </summary>
public interface IIdentitySeeder
{
    /// <summary>
    /// Creates whatever is missing, and does nothing at all when seeding is switched off.
    /// </summary>
    /// <exception cref="InvalidOperationException">Seeding is enabled outside Development, or a create
    /// operation was refused. A rejected password used to look like a clean start-up.</exception>
    Task SeedAsync(CancellationToken cancellationToken = default);
}
