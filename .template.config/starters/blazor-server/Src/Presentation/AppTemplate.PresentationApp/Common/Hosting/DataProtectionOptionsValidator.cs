using Microsoft.Extensions.Options;

namespace AppTemplate.PresentationApp.Common.Hosting;

internal sealed class DataProtectionOptionsValidator(IHostEnvironment environment) : IValidateOptions<DataProtectionOptions>
{
    public ValidateOptionsResult Validate(string? name, DataProtectionOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        if (string.IsNullOrWhiteSpace(options.KeysDirectory))
        {
            return environment.IsDevelopment()
                ? ValidateOptionsResult.Success
                : ValidateOptionsResult.Fail(
                    $"'{DataProtectionOptions.SectionName}:{nameof(options.KeysDirectory)}' is required outside Development: "
                    + "the session cookies and antiforgery tokens are protected by keys that must outlive a restart and be shared by every instance.");
        }

        return Path.IsPathFullyQualified(options.KeysDirectory)
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail(
                $"'{DataProtectionOptions.SectionName}:{nameof(options.KeysDirectory)}' must be an absolute path.");
    }
}
