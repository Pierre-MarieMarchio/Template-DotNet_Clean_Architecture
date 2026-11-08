using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace AppTemplate.Api.Common.Errors;

internal static class ProblemDetailsNormaliser
{
    internal const string FallbackCode = "request.failed";

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
