using Microsoft.Extensions.Options;

namespace AppTemplate.Infrastructure.Core.Common.Options;

/// <summary>Refuses a pool bound or a timeout outside what one server can answer.</summary>
public sealed class DatabaseOptionsValidator : IValidateOptions<DatabaseOptions>
{
    /// <summary>Validates the bound section at start-up.</summary>
    /// <param name="name">The named options instance, which this policy does not distinguish.</param>
    /// <param name="options">The bound values.</param>
    /// <returns>Success, or every failure at once.</returns>
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
