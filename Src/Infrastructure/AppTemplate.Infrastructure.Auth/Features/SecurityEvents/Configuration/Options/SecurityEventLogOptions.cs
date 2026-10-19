using Microsoft.Extensions.Options;

namespace AppTemplate.Infrastructure.Auth.Features.SecurityEvents.Configuration.Options;

/// <summary>How long the security-event trail is kept, and how the purge removes what has aged out.</summary>
public sealed class SecurityEventLogOptions
{
    public const string SectionName = "SecurityEventLog";

    /// <summary>
    /// Days an event is kept before the purge deletes it — for every account, a deleted one included.
    /// Long enough to investigate a compromise reported weeks after it happened; short enough that the
    /// IP addresses and user agents it holds are not kept indefinitely.
    /// </summary>
    public int RetentionInDays { get; set; } = 90;

    /// <summary>
    /// Rows one delete statement removes. The purge repeats until a statement removes fewer, so a
    /// backlog is cleared in short transactions rather than one long lock over the whole table.
    /// </summary>
    public int PurgeBatchSize { get; set; } = 1000;
}

internal sealed class SecurityEventLogOptionsValidator : IValidateOptions<SecurityEventLogOptions>
{
    public ValidateOptionsResult Validate(string? name, SecurityEventLogOptions options)
    {
        var failures = new List<string>();

        if (options.RetentionInDays is < 1 or > 3650)
        {
            failures.Add($"'{SecurityEventLogOptions.SectionName}:RetentionInDays' must be between 1 and 3650.");
        }

        if (options.PurgeBatchSize is < 1 or > 100_000)
        {
            failures.Add($"'{SecurityEventLogOptions.SectionName}:PurgeBatchSize' must be between 1 and 100000.");
        }

        return failures.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(failures);
    }
}
