using AppTemplate.Application.Core.Common.Localization;
using Microsoft.Extensions.Options;

namespace AppTemplate.Presentation.Core.Common.Localization;

/// <summary>
/// The language this deployment writes to a reader who has not said which one they read.
/// <para>
/// There is deliberately <b>no list of supported languages here</b>. What a mail can be written in
/// is what templates the infrastructure modules embed, and a list in configuration would be a
/// second statement of that — free to name a language no template backs, or to omit one that ships.
/// A request asking for a language nothing was written in falls back on its own.
/// </para>
/// <para>
/// Public because it is bound from configuration and its section name is part of the template's
/// contract with whoever deploys it. One class for every host, binding one section: two of them
/// could disagree about what an unattributed mail reads like, and a deployment that wrote a
/// password reset and a reminder in different languages would be right to call that a bug.
/// </para>
/// </summary>
public sealed class LocalizationOptions
{
    /// <summary>The configuration section this binds, and part of the deployment contract.</summary>
    public const string SectionName = "Localization";

    /// <summary>
    /// A culture name — <c>en</c>, <c>fr</c>, <c>fr-CA</c>. Used wherever a host has nothing more
    /// specific to go on: a request carrying no usable <c>Accept-Language</c>, or a process with no
    /// request at all, for which it is the language of every mail it sends.
    /// </summary>
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

        // Shape only, and deliberately: this repository builds with InvariantGlobalization, so the
        // runtime knows no culture but the invariant one and cannot be asked whether 'fr' is real.
        // Whether a tag names a language this deployment can write is a different question with a
        // different answer — which templates ship — and the renderers fall back on their own.
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
