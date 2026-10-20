using System.Diagnostics;

namespace AppTemplate.Api.Common.Observability;

internal sealed class RequestLoggingMiddleware(RequestDelegate next, ILogger<RequestLoggingMiddleware> logger)
{
    public async Task InvokeAsync(HttpContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        long started = Stopwatch.GetTimestamp();

        try
        {
            await next(context);
        }
        finally
        {
            if (logger.IsEnabled(LogLevel.Information))
            {
                logger.LogInformation(
                    "{Method} {Path} responded {StatusCode} in {ElapsedMilliseconds}ms " +
                    "(traceIdentifier {TraceIdentifier}, trace {TraceId}).",
                    context.Request.Method,
                    context.Request.Path.Value,
                    context.Response.StatusCode,
                    Stopwatch.GetElapsedTime(started).TotalMilliseconds,
                    context.TraceIdentifier,
                    Activity.Current?.TraceId.ToString());
            }
        }
    }
}
