using System.Diagnostics;

namespace AppTemplate.Api.Common.Observability;

/// <summary>
/// One structured entry per request, carrying both identifiers a failure is investigated with.
/// </summary>
/// <remarks>
/// <para>
/// <b>Redaction guarantee.</b> This middleware logs the fixed set of fields named in
/// <see cref="InvokeAsync"/> and nothing else. It never enumerates
/// <see cref="HttpRequest.Headers"/>, never touches <see cref="HttpRequest.Cookies"/>, and never
/// reads <see cref="HttpRequest.Body"/> — so an <c>Authorization</c> header, a session cookie, and
/// the password and refresh token that the authentication endpoints take in their JSON bodies have no
/// path to a log sink through here. <see cref="HttpRequest.QueryString"/> is excluded for the same
/// reason: a caller chooses its contents.
/// </para>
/// <para>
/// <b>Why two identifiers.</b> <c>TraceIdentifier</c> is the value the API puts in the
/// <c>traceId</c> member of every ProblemDetails response, so it is what a caller quotes.
/// <c>TraceId</c> is the W3C trace the exporter sends, so it is what a trace is looked up by. Logging
/// both in one entry is what turns a caller's <c>traceId</c> into a trace. When OpenTelemetry is
/// enabled the same <c>TraceIdentifier</c> is also set as a tag on the request span, so the join
/// works from either end.
/// </para>
/// </remarks>
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
