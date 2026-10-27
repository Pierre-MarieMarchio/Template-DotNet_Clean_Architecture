using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;

namespace AppTemplate.Api.Common.OpenApi;

internal sealed class OpenApiProblemSchemaTransformer : IOpenApiSchemaTransformer
{
    private static readonly string[] _alwaysWritten = ["type", "code", "traceId"];

    public Task TransformAsync(
        OpenApiSchema schema,
        OpenApiSchemaTransformerContext context,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(schema);
        ArgumentNullException.ThrowIfNull(context);

        if (!typeof(ProblemDetails).IsAssignableFrom(context.JsonTypeInfo.Type))
        {
            return Task.CompletedTask;
        }

        schema.Properties ??= new Dictionary<string, IOpenApiSchema>(StringComparer.Ordinal);
        schema.Properties["type"] = new OpenApiSchema { Type = JsonSchemaType.String };
        schema.Properties["code"] = new OpenApiSchema { Type = JsonSchemaType.String };
        schema.Properties["traceId"] = new OpenApiSchema { Type = JsonSchemaType.String };

        schema.Required ??= new HashSet<string>(StringComparer.Ordinal);

        foreach (string name in _alwaysWritten)
        {
            schema.Required.Add(name);
        }

        return Task.CompletedTask;
    }
}
