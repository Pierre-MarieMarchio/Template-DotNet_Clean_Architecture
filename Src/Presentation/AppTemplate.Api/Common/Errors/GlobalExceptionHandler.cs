using AppTemplate.Application.Common;
using AppTemplate.Domain.Common.Exceptions;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;

namespace AppTemplate.Api.Common.Errors;

/// <summary>
/// Last-resort handler for anything that escapes a use case. It answers
/// <c>application/problem+json</c> with the same stable <c>code</c> extension as
/// <see cref="ErrorResults"/>, and never puts <c>exception.Message</c> in the response.
/// </summary>
internal sealed class GlobalExceptionHandler(ILogger<GlobalExceptionHandler> logger) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(
        HttpContext httpContext,
        Exception exception,
        CancellationToken cancellationToken)
    {
        var (status, title, code, detail) = exception switch
        {
            ConcurrencyConflictException => (
                StatusCodes.Status409Conflict,
                "Conflict",
                "concurrency.conflict",
                "The resource was changed by another request. Reload it and apply the change again."),

            // 400 rather than 500: a DomainException is a caller driving an aggregate into a
            // forbidden state, so the response describes the request, not the defect behind it.
            //
            // This branch is a net, not a path. Every write use case already catches
            // DomainException at its own boundary and returns a Result, so reaching here means one
            // of them forgot to — and a 400 naming a broken rule beats a 500 naming nothing. Note
            // that the type is only visible through AppTemplate.Application's own reference to the domain;
            // that is deliberate, so do not "fix" the missing ProjectReference to AppTemplate.Domain, and do
            // not delete this arm because nothing appears to exercise it.
            DomainException => (
                StatusCodes.Status400BadRequest,
                "Invalid request",
                "domain.invariantViolated",
                "The request could not be completed because it violates a business rule."),
            OperationCanceledException => (
                StatusCodes.Status499ClientClosedRequest,
                "Request cancelled",
                "request.cancelled",
                "The client closed the request before it completed."),
            _ => (
                StatusCodes.Status500InternalServerError,
                "Unexpected error",
                "server.unexpected",
                "An unexpected error occurred. Contact support with the trace identifier if it persists."),
        };

        if (status == StatusCodes.Status499ClientClosedRequest)
        {
            // No response can be written to a client that has hung up, and nothing is worth
            // alerting on. The IsEnabled guard keeps the PathString allocation off the hot path
            // when Information logging is off (CA1873).
            if (logger.IsEnabled(LogLevel.Information))
            {
                logger.LogInformation("Request {Path} was cancelled by the client.", httpContext.Request.Path);
            }

            return true;
        }

        if (status == StatusCodes.Status409Conflict)
        {
            // A lost update is a normal outcome of concurrent writers, not a defect to alert on.
            if (logger.IsEnabled(LogLevel.Warning))
            {
                logger.LogWarning(
                    exception,
                    "Concurrency conflict while handling {Method} {Path}.",
                    httpContext.Request.Method,
                    httpContext.Request.Path);
            }
        }
        else
        {
            logger.LogError(
                exception,
                "Unhandled {ExceptionType} while handling {Method} {Path}.",
                exception.GetType().Name,
                httpContext.Request.Method,
                httpContext.Request.Path);
        }

        var problem = new ProblemDetails
        {
            Status = status,
            Title = title,
            Detail = detail,
        };

        problem.Extensions["code"] = code;
        ProblemDetailsDefaults.Normalise(problem, httpContext);

        httpContext.Response.StatusCode = status;
        await httpContext.Response.WriteAsJsonAsync(
            problem,
            options: null,
            contentType: "application/problem+json",
            cancellationToken);

        return true;
    }
}
