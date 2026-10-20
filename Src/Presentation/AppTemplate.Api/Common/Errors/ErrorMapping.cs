using AppTemplate.Application.Core.Common.Results;
using Microsoft.AspNetCore.Mvc;

namespace AppTemplate.Api.Common.Errors;

public static class ErrorMapping
{
    public static ActionResult ToActionResult(this Error error) => Build(error, httpContext: null);

    public static ActionResult ToActionResult(this Error error, HttpContext httpContext)
    {
        ArgumentNullException.ThrowIfNull(httpContext);

        return Build(error, httpContext);
    }

    private static ObjectResult Build(Error error, HttpContext? httpContext)
    {
        ArgumentNullException.ThrowIfNull(error);

        int status = StatusCodeFor(error.Type);

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

        problem.Extensions["code"] = error.Code;

        if (httpContext is not null)
        {
            ProblemDetailsNormaliser.Normalise(problem, httpContext);
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
