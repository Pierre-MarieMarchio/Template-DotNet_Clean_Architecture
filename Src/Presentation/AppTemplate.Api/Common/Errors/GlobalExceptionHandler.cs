using AppTemplate.Application.Common.Concurrency;
using AppTemplate.Domain.Common.Exceptions;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Http.Timeouts;
using Microsoft.AspNetCore.Mvc;

namespace AppTemplate.Api.Common.Errors;

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
        // A request timeout expires by cancelling the same RequestAborted token a client disconnect
        // cancels, so the exception type alone cannot tell a deadline from a hangup. This feature is
        // the only thing that can.
        //
        // Like the DomainException arm below, this is a net rather than a path: with the framework's
        // own RequestTimeoutsMiddleware, a timeout is answered by the policy's WriteTimeoutResponse
        // (see HostLifecycleExtensions) while the response has not started, and once it has, the middleware
        // clears this feature before rethrowing — and ExceptionHandlerMiddleware skips every handler
        // on a started response anyway. So this arm does not fire today. It is what keeps a deadline
        // from being logged and measured as a client hangup should anything ever relay one here, and
        // that misclassification is precisely the failure this file exists to prevent.
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
            // ExceptionHandlerMiddleware has already put 500 on the response before calling this
            // method; nothing overwrites that below because this branch returns early. Left alone,
            // every client cancellation would report as a server error to both the request log
            // (which reads the status this call leaves behind) and the request-duration metric —
            // the 5xx rate would be dominated by callers hanging up, not by anything worth paging on.
            // No body follows: nothing can be written to a client that has already hung up, and
            // HasStarted guards against a status change once the framework has begun the response.
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
            // Unlike a client hangup, this is the service failing to keep its own deadline —
            // loud enough to alert on, distinct enough from 500 to page differently.
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
