using AppTemplate.Application.Common.Localization;
using Microsoft.Extensions.Options;

namespace AppTemplate.Worker.Common.Localization;

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
/// contract with whoever deploys it. This is the twin of <c>AppTemplate.Api</c>'s class of the same
/// name, binding the same section: this host does not reference that one, and the duplication is
/// intended — a divergence between them is a bug, because it would mean the same deployment writes
/// a password reset and a reminder in different languages.
/// </para>
/// </summary>
public sealed class LocalizationOptions
{
    public const string SectionName = "Localization";

    /// <summary>
    /// A culture name — <c>en</c>, <c>fr</c>, <c>fr-CA</c>. This host has no request to read a
    /// reader's own language from, so it is the language of every mail its loops send.
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
