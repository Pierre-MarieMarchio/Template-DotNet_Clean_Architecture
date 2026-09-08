using Microsoft.Extensions.Options;

namespace AppTemplate.Api.Core.Common.Errors;

/// <summary>
/// The base URI <see cref="ProblemTypes"/> builds every <c>type</c> member from.
/// <para>
/// Public because it is bound from configuration and its section name is part of the template's
/// contract with whoever deploys it.
/// </para>
/// </summary>
public sealed class ProblemTypeOptions
{
    /// <summary>The configuration section this type is bound from.</summary>
    public const string SectionName = "ProblemTypes";

    /// <summary>
    /// Where a problem <c>type</c> URI is rooted. A deployment that publishes its own error
    /// documentation points this at it; the default is a URN, which identifies without promising a
    /// page that resolves.
    /// </summary>
    public string BaseUri { get; set; } = ProblemTypes.DefaultBaseUri;
}

internal sealed class ProblemTypeOptionsValidator : IValidateOptions<ProblemTypeOptions>
{
    public ValidateOptionsResult Validate(string? name, ProblemTypeOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        if (string.IsNullOrWhiteSpace(options.BaseUri)
            || !Uri.TryCreate(options.BaseUri, UriKind.Absolute, out _))
        {
            return ValidateOptionsResult.Fail(
                $"'{ProblemTypeOptions.SectionName}:{nameof(ProblemTypeOptions.BaseUri)}' must be an "
                + "absolute URI.");
        }

        return ValidateOptionsResult.Success;
    }
}
