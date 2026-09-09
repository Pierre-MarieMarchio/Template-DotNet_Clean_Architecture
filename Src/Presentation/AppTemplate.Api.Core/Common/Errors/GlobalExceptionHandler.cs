using AppTemplate.Application.Core.Common.Concurrency;
using AppTemplate.Domain.Core.Common.Exceptions;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Timeouts;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;

namespace AppTemplate.Api.Core.Common.Errors;

/// <summary>
/// Last-resort handler for anything that escapes a use case. It answers
/// <c>application/problem+json</c> with the same stable <c>code</c> extension as
/// <see cref="ErrorMapping"/>, and never puts <c>exception.Message</c> in the response.
/// </summary>
internal sealed class GlobalExceptionHandler(ILogger<GlobalExceptionHandler> logger) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(
        HttpContext httpContext,
        Exception exception,
        CancellationToken cancellationToken)
    {
        // A timeout cancels the same RequestAborted token a client disconnect does, so the exception
        // type cannot tell a deadline from a hangup and this feature is the only thing that can.
        // A net rather than a path: RequestTimeoutsMiddleware answers a timeout itself, so nothing
        // reaches this arm today. It keeps a deadline from being measured as a client hangup.
        bool isServerTimeout = exception is OperationCanceledException
            && httpContext.Features.Get<IHttpRequestTimeoutFeature>() is not null;

        var (status, title, code, detail) = exception switch
        {
            ConcurrencyConflictException => (
                StatusCodes.Status409Conflict,
                "Conflict",
                "concurrency.conflict",
                "The resource was changed by another request. Reload it and apply the change again."),

            // 400 rather than 500: a DomainException is a caller driving an aggregate into a
            // forbidden state. A net, not a path — every write use case catches it at its own
            // boundary — so nothing appears to exercise this arm, and it must stay. The type is
            // visible only through AppTemplate.Application's reference to the domain, which is why
            // this project has no ProjectReference to AppTemplate.Domain.
            DomainException => (
                StatusCodes.Status400BadRequest,
                "Invalid request",
                "domain.invariantViolated",
                "The request could not be completed because it violates a business rule."),
            OperationCanceledException when isServerTimeout => (
                StatusCodes.Status504GatewayTimeout,
                "Request timeout",
                "request.timeout",
                "The server did not complete the request within its configured timeout."),
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
            // ExceptionHandlerMiddleware has already put 500 on the response, and left alone every
            // client cancellation would report as a server error to the request log and the duration
            // metric. No body follows: the client has hung up.
            if (!httpContext.Response.HasStarted)
            {
                httpContext.Response.StatusCode = status;
            }

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
        else if (status == StatusCodes.Status504GatewayTimeout)
        {
            // The service failing its own deadline, not a client hanging up: loud enough to alert on.
            if (logger.IsEnabled(LogLevel.Warning))
            {
                logger.LogWarning(
                    exception,
                    "Request {Method} {Path} exceeded its request timeout.",
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
        ProblemDetailsNormaliser.Normalise(problem, httpContext);

        httpContext.Response.StatusCode = status;
        await httpContext.Response.WriteAsJsonAsync(
            problem,
            options: null,
            contentType: "application/problem+json",
            cancellationToken);

        return true;
    }
}
