using Microsoft.Extensions.Options;

namespace AppTemplate.Api.Common.Errors;

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
