using System.Text.Json;
using AppTemplate.Application.Core.Common.Results;
using FluentValidation.Results;

namespace AppTemplate.Application.Core.Common.Validation;

public static class ValidationError
{
    public const string Code = "request.validationFailed";

    private const string _message = "One or more fields are invalid.";

    public static Error From(ValidationResult validationResult)
    {
        ArgumentNullException.ThrowIfNull(validationResult);

        var details = validationResult.Errors
            .GroupBy(failure => NormalizePath(failure.PropertyName), StringComparer.Ordinal)
            .ToDictionary(
                group => group.Key,
                group => (IReadOnlyList<string>)group.Select(failure => failure.ErrorMessage).ToList(),
                StringComparer.Ordinal);

        return Error.Validation(Code, _message, details);
    }

    public static Error ForField(string field, string message)
    {
        ArgumentNullException.ThrowIfNull(field);
        ArgumentNullException.ThrowIfNull(message);

        var details = new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal)
        {
            [field] = [message],
        };

        return Error.Validation(Code, _message, details);
    }

    public static Error ForFields(IReadOnlyDictionary<string, IReadOnlyList<string>> details)
    {
        ArgumentNullException.ThrowIfNull(details);

        return Error.Validation(Code, _message, details);
    }

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
