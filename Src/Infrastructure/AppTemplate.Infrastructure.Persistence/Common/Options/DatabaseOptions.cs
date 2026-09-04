using Microsoft.Extensions.Options;

namespace AppTemplate.Infrastructure.Persistence.Common.Options;

/// <summary>
/// Connection pooling and command-timeout policy for <see cref="Common.Contexts.AppDbContext"/>.
/// <para>
/// <b>Why this exists.</b> Npgsql defaults <c>Maximum Pool Size</c> to 100 per process, and
/// PostgreSQL's own <c>max_connections</c> defaults to 100 for the whole server. Two replicas at
/// the driver default are already enough to exhaust it, and <c>AddDbContextFactory</c> makes the
/// arithmetic worse: <see cref="Common.Idempotency.IdempotencyStore"/> opens its own connection
/// per call, separate from the ambient request context, so one idempotent write can hold up to
/// three connections at once instead of one. See docs/CONFIGURATION.md for how to size
/// <c>MaxPoolSize</c> against replica count and PostgreSQL's <c>max_connections</c>.
/// </para>
/// </summary>
public sealed class DatabaseOptions
{
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

internal sealed class DatabaseOptionsValidator : IValidateOptions<DatabaseOptions>
{
    public ValidateOptionsResult Validate(string? name, DatabaseOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        var failures = new List<string>();

        if (options.MaxPoolSize is < 1 or > 500)
        {
            failures.Add($"'{DatabaseOptions.SectionName}:MaxPoolSize' must be between 1 and 500.");
        }

        if (options.CommandTimeoutSeconds is < 1 or > 300)
        {
            failures.Add($"'{DatabaseOptions.SectionName}:CommandTimeoutSeconds' must be between 1 and 300.");
        }

        return failures.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(failures);
    }
}
