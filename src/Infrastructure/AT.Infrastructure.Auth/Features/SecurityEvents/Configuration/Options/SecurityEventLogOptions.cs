using Microsoft.Extensions.Options;

namespace AppTemplate.Infrastructure.Auth.Features.SecurityEvents.Configuration.Options;

public sealed class SecurityEventLogOptions
{
    public const string SectionName = "SecurityEventLog";

    public int RetentionInDays { get; set; } = 90;

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
