using AppTemplate.Application.Core.Common.Concurrency;
using AppTemplate.Domain.Core.Common.Exceptions;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Http.Timeouts;
using Microsoft.AspNetCore.Mvc;

namespace AppTemplate.Api.Common.Errors;

internal sealed class GlobalExceptionHandler(ILogger<GlobalExceptionHandler> logger) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(
        HttpContext httpContext,
        Exception exception,
        CancellationToken cancellationToken)
    {
        bool isServerTimeout = exception is OperationCanceledException
            && httpContext.Features.Get<IHttpRequestTimeoutFeature>() is not null;

        var (status, title, code, detail) = exception switch
        {
            ConcurrencyConflictException => (
                StatusCodes.Status409Conflict,
                "Conflict",
                "concurrency.conflict",
                "The resource was changed by another request. Reload it and apply the change again."),

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
