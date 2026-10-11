namespace AppTemplate.Infrastructure.Core.Common.Options;

/// <summary>Connection pooling and command-timeout policy, shared by every context in the system.
/// One value rather than one per context because Npgsql pools per connection string, so two
/// contexts on the same string share one pool bounded by <see cref="MaxPoolSize"/> — two modules
/// deriving the bound separately would silently give the deployment two pools. It exists because
/// Npgsql defaults <c>Maximum Pool Size</c> to 100 per process where PostgreSQL's
/// <c>max_connections</c> defaults to 100 for the whole server, and a context factory makes the
/// arithmetic worse: an idempotent write can hold up to three connections at once. See
/// docs/CONFIGURATION.md for sizing <c>MaxPoolSize</c> against replica count.</summary>
public sealed class DatabaseOptions
{
    /// <summary>The configuration section this binds from.</summary>
    public const string SectionName = "Database";

    /// <summary>
    /// Passed through as Npgsql's <c>Maximum Pool Size</c>. Deliberately well under the driver's own
    /// default of 100, so that running several replicas of this process (every API instance, with or without the jobs) against
    /// one PostgreSQL server does not, by itself, approach <c>max_connections</c>.
    /// </summary>
    public int MaxPoolSize { get; set; } = 20;

    /// <summary>Npgsql's per-command timeout. The driver's own default is also 30 seconds.</summary>
    public int CommandTimeoutSeconds { get; set; } = 30;
}
