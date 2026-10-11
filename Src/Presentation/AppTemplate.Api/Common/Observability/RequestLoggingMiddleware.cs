using System.Diagnostics;

namespace AppTemplate.Api.Common.Observability;

/// <summary>One structured entry per request, carrying both identifiers a failure is investigated with.</summary>
/// <remarks><b>Redaction guarantee:</b> this middleware logs the fixed set of fields named in
/// <see cref="InvokeAsync"/> and nothing else — it never enumerates <see cref="HttpRequest.Headers"/>,
/// touches <see cref="HttpRequest.Cookies"/> or reads <see cref="HttpRequest.Body"/>, so an
/// <c>Authorization</c> header, a session cookie and the authentication endpoints' JSON bodies have
/// no path to a log sink through here; <see cref="HttpRequest.QueryString"/> is excluded for the
/// same reason. <b>Why not <c>UseHttpLogging</c>:</b> its fields are chosen by flags and it carries
/// neither identifier as a property — redaction there is an allow-list one flag can widen, where
/// here nothing is read that could leak. <b>Why two identifiers:</b> <c>TraceIdentifier</c> is what
/// the API puts in the <c>traceId</c> member of every ProblemDetails response, so what a caller
/// quotes; <c>TraceId</c> is the W3C trace the exporter sends, so what a trace is looked up by —
/// logging both is what turns a caller's <c>traceId</c> into a trace.</remarks>
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
            // Severity is uniform: the exception handler already decides how loud a failure is, and a
            // second opinion here would only double-count it.
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
