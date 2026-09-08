namespace AppTemplate.Infrastructure.Core.Common.Options;

/// <summary>
/// Connection pooling and command-timeout policy, shared by every context in the system.
/// <para>
/// <b>Why one value rather than one per context.</b> Npgsql pools per connection string, so two
/// contexts built on the same string share one pool bounded by <see cref="MaxPoolSize"/>. Binding
/// this section once and applying it to both is what keeps that true: two modules deriving the
/// bound separately could disagree, and the deployment would silently get two pools of that size
/// instead of one.
/// </para>
/// <para>
/// <b>Why it exists at all.</b> Npgsql defaults <c>Maximum Pool Size</c> to 100 per process, and
/// PostgreSQL's own <c>max_connections</c> defaults to 100 for the whole server. Two replicas at
/// the driver default are already enough to exhaust it, and a context factory makes the arithmetic
/// worse: <c>IdempotencyStore</c> opens its own connection per call, separate from the ambient
/// request context, so one idempotent write can hold up to three connections at once instead of
/// one. See docs/CONFIGURATION.md for how to size <c>MaxPoolSize</c> against replica count and
/// PostgreSQL's <c>max_connections</c>.
/// </para>
/// </summary>
public sealed class DatabaseOptions
{
    /// <summary>The configuration section this binds from.</summary>
    public const string SectionName = "Database";

    /// <summary>
    /// Passed through as Npgsql's <c>Maximum Pool Size</c>. Deliberately well under the driver's own
    /// default of 100, so that running several replicas of this process (API and/or worker) against
    /// one PostgreSQL server does not, by itself, approach <c>max_connections</c>.
    /// </summary>
    public int MaxPoolSize { get; set; } = 20;

    /// <summary>Npgsql's per-command timeout. The driver's own default is also 30 seconds.</summary>
    public int CommandTimeoutSeconds { get; set; } = 30;
}
