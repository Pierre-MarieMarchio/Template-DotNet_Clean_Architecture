using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace AppTemplate.Api.Common.Errors;

/// <summary>
/// The single normaliser every <c>ProblemDetails</c> producer in this API funnels through, so a 400
/// from model binding, one from <see cref="ErrorMapping"/> and one from
/// <see cref="GlobalExceptionHandler"/> end up with the same three members filled in: <c>code</c>,
/// <c>traceId</c> and <c>type</c>.
/// <para>
/// The framework answers some requests before any of our code runs — a body that is not JSON, a
/// missing required property, a route segment that fails its <c>:guid</c> constraint, an unknown
/// verb, a media type nothing accepts. Those arrive as a bare <c>ProblemDetails</c>, so a client that
/// always reads <c>code</c> breaks on exactly the inputs most likely to be malformed by accident.
/// This fills the fields in for them too, and never replaces a value a producer already set.
/// </para>
/// </summary>
internal static class ProblemDetailsNormaliser
{
    /// <summary>Used when the status carries no more specific meaning.</summary>
    internal const string FallbackCode = "request.failed";

    /// <summary>
    /// Fills in <c>code</c>, <c>traceId</c> and <c>type</c> on <paramref name="problem"/>, never
    /// overwriting a value already present — a producer that knows a more specific <c>code</c> than
    /// the status alone implies has already won by the time this runs.
    /// </summary>
    internal static void Normalise(ProblemDetails problem, HttpContext context)
    {
        ArgumentNullException.ThrowIfNull(problem);
        ArgumentNullException.ThrowIfNull(context);

        if (!problem.Extensions.TryGetValue("code", out object? codeValue))
        {
            int status = problem.Status ?? context.Response.StatusCode;
            codeValue = CodeFor(status);
            problem.Extensions["code"] = codeValue;
        }

        problem.Extensions.TryAdd("traceId", context.TraceIdentifier);

        if (string.IsNullOrEmpty(problem.Type))
        {
            string baseUri = context.RequestServices
                .GetRequiredService<IOptions<ProblemTypeOptions>>()
                .Value.BaseUri;

            problem.Type = ProblemTypes.For((string)codeValue!, baseUri);
        }
    }

    internal static string CodeFor(int status) => status switch
    {
        StatusCodes.Status400BadRequest => "request.malformed",
        StatusCodes.Status401Unauthorized => "auth.required",
        StatusCodes.Status403Forbidden => "auth.forbidden",
        StatusCodes.Status404NotFound => "route.notFound",
        StatusCodes.Status405MethodNotAllowed => "request.methodNotAllowed",
        StatusCodes.Status406NotAcceptable => "request.notAcceptable",
        StatusCodes.Status413PayloadTooLarge => "request.tooLarge",
        StatusCodes.Status415UnsupportedMediaType => "request.unsupportedMediaType",
        StatusCodes.Status500InternalServerError => "server.unexpected",
        _ => FallbackCode,
    };
}
