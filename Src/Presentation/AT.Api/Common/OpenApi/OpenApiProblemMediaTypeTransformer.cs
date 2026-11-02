using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;

namespace AppTemplate.Api.Common.OpenApi;

internal sealed class OpenApiProblemMediaTypeTransformer : IOpenApiOperationTransformer
{
    private const string _problemMediaType = "application/problem+json";

    public Task TransformAsync(
        OpenApiOperation operation,
        OpenApiOperationTransformerContext context,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(operation);

        foreach (var response in operation.Responses?.Values.OfType<OpenApiResponse>() ?? [])
        {
            var problem = response.Content?.Values
                .FirstOrDefault(media => media.Schema is OpenApiSchemaReference { Reference.Id: "ProblemDetails" or "ValidationProblemDetails" });

            if (problem is not null)
            {
                response.Content = new Dictionary<string, OpenApiMediaType>(StringComparer.Ordinal)
                {
                    [_problemMediaType] = problem,
                };
            }
        }

        return Task.CompletedTask;
    }
}
