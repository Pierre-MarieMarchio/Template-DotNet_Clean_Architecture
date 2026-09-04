using AppTemplate.Application.Common;
using Microsoft.AspNetCore.Mvc;

namespace AppTemplate.Api.Common.Errors;

/// <summary>
/// The single place where an application <see cref="Error"/> becomes an HTTP response, so that
/// one <see cref="ErrorType"/> always produces the same status and the same body shape.
/// </summary>
public static class ErrorResults
{
    public static ActionResult ToActionResult(this Error error)
    {
        ArgumentNullException.ThrowIfNull(error);

        int status = StatusCodeFor(error.Type);

        var problem = new ProblemDetails
        {
            Status = status,
            Title = TitleFor(error.Type),
            Detail = error.Message,
            Type = $"https://httpstatuses.io/{status}",
        };

        // Clients branch on this, never on Detail.
        problem.Extensions["code"] = error.Code;

        return new ObjectResult(problem)
        {
            StatusCode = status,
            ContentTypes = { "application/problem+json" },
        };
    }

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
