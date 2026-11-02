using Microsoft.Extensions.Options;

namespace AppTemplate.Api.Common.Hosting;

public sealed class ShutdownOptions
{
    public const string SectionName = "Shutdown";

    public TimeSpan Timeout { get; set; } = TimeSpan.FromSeconds(30);
}

internal sealed class ShutdownOptionsValidator : IValidateOptions<ShutdownOptions>
{
    private static readonly TimeSpan _maxTimeout = TimeSpan.FromMinutes(10);

    public ValidateOptionsResult Validate(string? name, ShutdownOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        if (options.Timeout <= TimeSpan.Zero || options.Timeout > _maxTimeout)
        {
            return ValidateOptionsResult.Fail(
                $"'{ShutdownOptions.SectionName}:{nameof(ShutdownOptions.Timeout)}' must be greater " +
                $"than zero and at most {_maxTimeout}.");
        }

        return ValidateOptionsResult.Success;
    }
}
