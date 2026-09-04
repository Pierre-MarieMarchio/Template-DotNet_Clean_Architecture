using Microsoft.Extensions.Configuration;

namespace AppTemplate.Infrastructure.Persistence.Common.Contexts;

/// <summary>
/// The one connection string this system has. There is exactly one PostgreSQL database and exactly
/// one <see cref="AppDbContext"/>, so exactly one migrations history; the features separate
/// themselves by schema rather than by connection. A second name for this database would be a
/// second thing to configure, and the two could disagree without anything noticing until runtime.
/// </summary>
public static class DefaultConnectionString
{
    /// <summary>The configuration key, under <c>ConnectionStrings</c>.</summary>
    public const string Name = "Default";

    /// <summary>The environment variable that overrides it, in the standard nested form.</summary>
    public const string EnvironmentVariableName = "ConnectionStrings__Default";

    /// <summary>
    /// Points at the local docker-compose database. Design-time only: it is used to pick a
    /// provider and never to reach a real server when scaffolding a migration, which is why
    /// it is a visible local default rather than a secret.
    /// </summary>
    private const string _localDesignTimeFallback =
        "Host=localhost;Port=5432;Database=appdb;Username=postgres;Password=postgres";

    /// <summary>
    /// Reads the connection string, failing at composition time rather than on the first
    /// request that needs a database.
    /// </summary>
    /// <exception cref="InvalidOperationException">The key is absent or blank.</exception>
    public static string Require(IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        string? connectionString = configuration.GetConnectionString(Name);

        return string.IsNullOrWhiteSpace(connectionString)
            ? throw new InvalidOperationException(
                $"The '{Name}' connection string is not configured. Set ConnectionStrings:{Name}.")
            : connectionString;
    }

    /// <summary>
    /// Resolves a connection string for <c>dotnet ef</c>, which has no host and therefore no
    /// configuration. A real target can be pointed at with
    /// <see cref="EnvironmentVariableName"/>; otherwise the local default is used, because a
    /// design-time factory that threw would make <c>migrations add</c> impossible on a clean
    /// checkout.
    /// </summary>
    public static string ResolveForDesignTime()
    {
        string? connectionString = Environment.GetEnvironmentVariable(EnvironmentVariableName);

        return string.IsNullOrWhiteSpace(connectionString) ? _localDesignTimeFallback : connectionString;
    }
}
