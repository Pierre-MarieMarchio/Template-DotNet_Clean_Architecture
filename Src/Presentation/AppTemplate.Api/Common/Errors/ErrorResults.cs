using AppTemplate.Application.Common;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace AppTemplate.Api.Common.Errors;

/// <summary>
/// The single place where an application <see cref="Error"/> becomes an HTTP response, so that
/// one <see cref="ErrorType"/> always produces the same status and the same body shape.
/// </summary>
public static class ErrorResults
{
    /// <summary>
    /// Without a request to normalise against: no <c>traceId</c>, and <c>type</c> falls back to
    /// <see cref="ProblemTypes.DefaultBaseUri"/> rather than whatever <see cref="ProblemTypeOptions"/>
    /// configures. Prefer <see cref="ToActionResult(Error, HttpContext)"/> everywhere a
    /// <see cref="HttpContext"/> is available.
    /// </summary>
    public static ActionResult ToActionResult(this Error error) => Build(error, httpContext: null);

    /// <summary>Normalises the response through <see cref="ProblemDetailsDefaults.Normalise"/>.</summary>
    public static ActionResult ToActionResult(this Error error, HttpContext httpContext)
    {
        ArgumentNullException.ThrowIfNull(httpContext);

        return Build(error, httpContext);
    }

    private static ObjectResult Build(Error error, HttpContext? httpContext)
    {
        ArgumentNullException.ThrowIfNull(error);

        int status = StatusCodeFor(error.Type);

        // A dictionary of per-field failures is what turns this into a validation problem: the same
        // distinction ModelStateProblemDetails makes for the errors MVC's own binding produces.
        ProblemDetails problem = error.Details is { Count: > 0 } details
            ? new ValidationProblemDetails(ToFieldErrors(details))
            {
                Status = status,
                Title = TitleFor(error.Type),
                Detail = error.Message,
            }
            : new ProblemDetails
            {
                Status = status,
                Title = TitleFor(error.Type),
                Detail = error.Message,
            };

        // Clients branch on this, never on Detail.
        problem.Extensions["code"] = error.Code;

        if (httpContext is not null)
        {
            ProblemDetailsDefaults.Normalise(problem, httpContext);
        }
        else
        {
            problem.Type = ProblemTypes.For(error.Code);
        }

        return new ObjectResult(problem)
        {
            StatusCode = status,
            ContentTypes = { "application/problem+json" },
        };
    }

    // Keys are already camelCase: ValidationError.From normalises them once, at the point the
    // failure is authored, and this must not re-transform what is already in the right shape.
    private static Dictionary<string, string[]> ToFieldErrors(
        IReadOnlyDictionary<string, IReadOnlyList<string>> details) =>
        details.ToDictionary(pair => pair.Key, pair => pair.Value.ToArray(), StringComparer.Ordinal);

    private static int StatusCodeFor(ErrorType type) => type switch
    {
        ErrorType.Validation => StatusCodes.Status400BadRequest,
        ErrorType.Unauthorized => StatusCodes.Status401Unauthorized,
        ErrorType.Forbidden => StatusCodes.Status403Forbidden,
        ErrorType.NotFound => StatusCodes.Status404NotFound,
        ErrorType.Conflict => StatusCodes.Status409Conflict,
        ErrorType.TooManyRequests => StatusCodes.Status429TooManyRequests,
        ErrorType.PreconditionFailed => StatusCodes.Status412PreconditionFailed,
        ErrorType.PreconditionRequired => StatusCodes.Status428PreconditionRequired,
        _ => StatusCodes.Status500InternalServerError,
    };

    private static string TitleFor(ErrorType type) => type switch
    {
        ErrorType.Validation => "Invalid request",
        ErrorType.Unauthorized => "Unauthorized",
        ErrorType.Forbidden => "Forbidden",
        ErrorType.NotFound => "Not found",
        ErrorType.Conflict => "Conflict",
        ErrorType.TooManyRequests => "Too many requests",
        ErrorType.PreconditionFailed => "Precondition failed",
        ErrorType.PreconditionRequired => "Precondition required",
        _ => "Unexpected error",
    };
}
