using System.Text.Json;
using FluentValidation.Results;

namespace AppTemplate.Application.Common.Validation;

/// <summary>Builds the single <see cref="Error"/> shape every validation failure is reported as.</summary>
public static class ValidationError
{
    public const string Code = "request.validationFailed";

    private const string Message = "One or more fields are invalid.";

    /// <summary>
    /// Groups <paramref name="validationResult"/>'s failures by field. The message is fixed rather
    /// than a concatenation of the failures: the per-field text belongs in <see cref="Error.Details"/>,
    /// not folded into a sentence a client would have to re-parse.
    /// </summary>
    public static Error From(ValidationResult validationResult)
    {
        ArgumentNullException.ThrowIfNull(validationResult);

        var details = validationResult.Errors
            .GroupBy(failure => NormalizePath(failure.PropertyName), StringComparer.Ordinal)
            .ToDictionary(
                group => group.Key,
                group => (IReadOnlyList<string>)group.Select(failure => failure.ErrorMessage).ToList(),
                StringComparer.Ordinal);

        return Error.Validation(Code, Message, details);
    }

    /// <summary>For a rule a validator cannot express, e.g. one the store owns.</summary>
    public static Error ForField(string field, string message)
    {
        ArgumentNullException.ThrowIfNull(field);
        ArgumentNullException.ThrowIfNull(message);

        var details = new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal)
        {
            [field] = [message],
        };

        return Error.Validation(Code, Message, details);
    }

    /// <summary>
    /// Camel-cases each segment of a FluentValidation property path, e.g. <c>Tags[0]</c> becomes
    /// <c>tags[0]</c> and <c>Address.City</c> becomes <c>address.city</c> — the indexer suffix is
    /// left untouched, since <see cref="JsonNamingPolicy.CamelCase"/> only knows identifiers.
    /// </summary>
    private static string NormalizePath(string propertyName) =>
        string.Join('.', propertyName.Split('.').Select(NormalizeSegment));

    private static string NormalizeSegment(string segment)
    {
        int bracketIndex = segment.IndexOf('[', StringComparison.Ordinal);

        if (bracketIndex < 0)
        {
            return JsonNamingPolicy.CamelCase.ConvertName(segment);
        }

        string identifier = segment[..bracketIndex];
        string suffix = segment[bracketIndex..];

        return JsonNamingPolicy.CamelCase.ConvertName(identifier) + suffix;
    }
}
