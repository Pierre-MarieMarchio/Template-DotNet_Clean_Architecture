using AppTemplate.Client.Generated.Models;
using Microsoft.Kiota.Abstractions;
using Microsoft.Kiota.Abstractions.Serialization;

namespace AppTemplate.Client.Common.Errors;

public sealed record ApiProblem(
    int Status,
    string Code,
    string? Detail,
    string? TraceId,
    IReadOnlyDictionary<string, IReadOnlyList<string>> FieldErrors)
{
    public const string UnreadableCode = "response.unreadable";

    private static readonly IReadOnlyDictionary<string, IReadOnlyList<string>> _noFieldErrors =
        new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal);

    public static ApiProblem From(ApiException error)
    {
        ArgumentNullException.ThrowIfNull(error);

        return error switch
        {
            ValidationProblemDetails { Code: { Length: > 0 } code } validation => new ApiProblem(
                error.ResponseStatusCode,
                code,
                validation.Detail ?? validation.Title,
                validation.TraceId,
                FieldErrorsOf(validation.Errors)),
            ProblemDetails { Code: { Length: > 0 } code } problem => new ApiProblem(
                error.ResponseStatusCode,
                code,
                problem.Detail ?? problem.Title,
                problem.TraceId,
                _noFieldErrors),
            _ => new ApiProblem(error.ResponseStatusCode, UnreadableCode, null, null, _noFieldErrors),
        };
    }

    public IReadOnlyList<string> ErrorsFor(string field) =>
        FieldErrors.TryGetValue(field, out var messages) ? messages : [];

    private static Dictionary<string, IReadOnlyList<string>> FieldErrorsOf(ValidationProblemDetails_errors? errors) =>
        (errors?.AdditionalData ?? new Dictionary<string, object>())
            .ToDictionary(
                field => field.Key,
                field => (IReadOnlyList<string>)MessagesOf(field.Value),
                StringComparer.Ordinal);

    private static string[] MessagesOf(object? value) =>
        value is UntypedArray array
            ? [.. array.GetValue().OfType<UntypedString>().Select(message => message.GetValue()).OfType<string>()]
            : [];
}
