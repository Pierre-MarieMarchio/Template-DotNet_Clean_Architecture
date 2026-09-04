using Microsoft.AspNetCore.Http;

namespace AppTemplate.Api.Common.Errors;

/// <summary>
/// Gives a <c>code</c> to the failures the application never decides on.
/// <para>
/// <see cref="ErrorResults"/> puts a stable <c>code</c> on every error the application authors, and
/// the API's contract is that a client branches on that field rather than on prose. But the
/// framework answers some requests before any of our code runs — a body that is not JSON, a missing
/// required property, a route segment that fails its <c>:guid</c> constraint, an unknown verb, a
/// media type nothing accepts. Those arrive as a bare <c>ProblemDetails</c>, so a client that always
/// reads <c>code</c> breaks on exactly the inputs most likely to be malformed by accident. This
/// fills the field in for them, and only for them: a <c>code</c> already present is never replaced.
/// </para>
/// </summary>
public static class ProblemDetailsDefaults
{
    /// <summary>Used when the status carries no more specific meaning.</summary>
    internal const string FallbackCode = "request.failed";

    public static IServiceCollection AddApiProblemDetails(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddProblemDetails(options => options.CustomizeProblemDetails = context =>
        {
            // Anything routed through ErrorResults or the exception handler already carries its own
            // code, which is more specific than anything derivable from a status alone.
            if (context.ProblemDetails.Extensions.ContainsKey("code"))
            {
                return;
            }

            int status = context.ProblemDetails.Status ?? context.HttpContext.Response.StatusCode;
            context.ProblemDetails.Extensions["code"] = CodeFor(status);
        });

        return services;
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
