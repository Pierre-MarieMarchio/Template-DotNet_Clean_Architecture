using Microsoft.Extensions.Options;

namespace AppTemplate.Api.Common.Errors;

/// <summary>
/// The base URI <see cref="ProblemTypes"/> builds every <c>type</c> member from.
/// <para>
/// Public because it is bound from configuration and its section name is part of the template's
/// contract with whoever deploys it.
/// </para>
/// </summary>
public sealed class ProblemTypeOptions
{
    public const string SectionName = "ProblemTypes";

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
