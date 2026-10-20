using AppTemplate.Application.Core.Common.Localization;
using Microsoft.Extensions.Options;

namespace AppTemplate.Presentation.Core.Common.Localization;

public sealed class LocalizationOptions
{
    public const string SectionName = "Localization";

    public string DefaultCulture { get; set; } = "en";
}

internal sealed class LocalizationOptionsValidator : IValidateOptions<LocalizationOptions>
{
    public ValidateOptionsResult Validate(string? name, LocalizationOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        if (string.IsNullOrWhiteSpace(options.DefaultCulture))
        {
            return ValidateOptionsResult.Fail(
                $"'{LocalizationOptions.SectionName}:{nameof(LocalizationOptions.DefaultCulture)}' is required.");
        }

        if (!CurrentLanguage.IsWellFormed(options.DefaultCulture))
        {
            return ValidateOptionsResult.Fail(
                $"'{LocalizationOptions.SectionName}:{nameof(LocalizationOptions.DefaultCulture)}' is "
                + $"'{options.DefaultCulture}', which is not a well-formed language tag such as 'en' "
                + "or 'fr-CA'.");
        }

        return ValidateOptionsResult.Success;
    }
}
