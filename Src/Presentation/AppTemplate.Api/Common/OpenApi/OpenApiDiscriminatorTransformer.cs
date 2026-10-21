using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;

namespace AppTemplate.Api.Common.OpenApi;

internal sealed class OpenApiDiscriminatorTransformer : IOpenApiSchemaTransformer
{
    public Task TransformAsync(
        OpenApiSchema schema,
        OpenApiSchemaTransformerContext context,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(schema);
        ArgumentNullException.ThrowIfNull(context);

        var baseType = context.JsonTypeInfo.Type.BaseType;

        if (baseType is null || baseType == typeof(object))
        {
            return Task.CompletedTask;
        }

        var polymorphism = context.JsonTypeInfo.Options.GetTypeInfo(baseType).PolymorphismOptions;

        if (polymorphism is null || schema.Properties?.ContainsKey(polymorphism.TypeDiscriminatorPropertyName) != true)
        {
            return Task.CompletedTask;
        }

        schema.Required ??= new HashSet<string>(StringComparer.Ordinal);
        schema.Required.Add(polymorphism.TypeDiscriminatorPropertyName);

        return Task.CompletedTask;
    }
}
