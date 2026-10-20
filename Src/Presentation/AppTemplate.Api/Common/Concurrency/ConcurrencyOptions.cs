using Microsoft.Extensions.Options;

namespace AppTemplate.Api.Common.Concurrency;

public sealed class ConcurrencyOptions
{
    public const string SectionName = "Concurrency";

    public IfMatchRequirement IfMatch { get; set; } = IfMatchRequirement.Optional;
}

internal sealed class ConcurrencyOptionsValidator : IValidateOptions<ConcurrencyOptions>
{
    public ValidateOptionsResult Validate(string? name, ConcurrencyOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        if (!Enum.IsDefined(options.IfMatch))
        {
            return ValidateOptionsResult.Fail(
                $"'{ConcurrencyOptions.SectionName}:{nameof(ConcurrencyOptions.IfMatch)}' is "
                + $"'{(int)options.IfMatch}', which is not one of "
                + $"'{nameof(IfMatchRequirement.Optional)}' or '{nameof(IfMatchRequirement.Required)}'.");
        }

        return ValidateOptionsResult.Success;
    }
}
