using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;

namespace AppTemplate.Api.Common.OpenApi;

internal sealed class OpenApiValidationProblemTransformer : IOpenApiDocumentTransformer
{
    private const string _schemaName = nameof(ValidationProblemDetails);

    public async Task TransformAsync(
        OpenApiDocument document,
        OpenApiDocumentTransformerContext context,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(context);

        document.Components ??= new OpenApiComponents();
        document.Components.Schemas ??= new Dictionary<string, IOpenApiSchema>(StringComparer.Ordinal);

        if (document.Components.Schemas.ContainsKey(_schemaName))
        {
            return;
        }

        var schema = await context.GetOrCreateSchemaAsync(typeof(ValidationProblemDetails), null, cancellationToken);

        document.Components.Schemas[_schemaName] = schema;
    }
}
