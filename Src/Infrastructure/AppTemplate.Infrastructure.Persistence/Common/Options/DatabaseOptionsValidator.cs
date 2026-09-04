using Microsoft.Extensions.Options;

namespace AppTemplate.Infrastructure.Persistence.Common.Options;

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
