using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;

namespace AppTemplate.Api.Common.OpenApi;

/// <summary>
/// Declares the bearer scheme on the OpenAPI document. The scheme is <c>Http</c>/<c>bearer</c>
/// rather than <c>ApiKey</c>, so the UI adds the <c>Bearer </c> prefix itself. No global security
/// requirement is added: a padlock on every operation would describe the document rather than the
/// runtime, where <c>[Authorize]</c> decides.
/// </summary>
internal sealed class OpenApiSecurityTransformer(IAuthenticationSchemeProvider schemeProvider)
    : IOpenApiDocumentTransformer
{
    public async Task TransformAsync(
        OpenApiDocument document,
        OpenApiDocumentTransformerContext context,
        CancellationToken cancellationToken)
    {
        var schemes = await schemeProvider.GetAllSchemesAsync();

        if (!schemes.Any(scheme => scheme.Name == "Bearer"))
        {
            return;
        }

        document.Components ??= new OpenApiComponents();
        document.Components.SecuritySchemes ??= new Dictionary<string, IOpenApiSecurityScheme>();
        document.Components.SecuritySchemes["Bearer"] = new OpenApiSecurityScheme
        {
            Type = SecuritySchemeType.Http,
            Scheme = "bearer",
            BearerFormat = "JWT",
            In = ParameterLocation.Header,
            Description = "JWT access token obtained from POST /api/v1/auth/login.",
        };
    }
}
