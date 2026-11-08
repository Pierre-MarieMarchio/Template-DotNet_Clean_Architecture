using AppTemplate.Api.Features.Auth.Contracts.Responses;
using AppTemplate.Api.Features.Auth.Controllers;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;

namespace AppTemplate.Api.Features.Auth.Composition;

internal static class BrowserSessionComposition
{
    private const string _refreshTokenProperty = "refreshToken";

    private const string _cookieAttributes = "HttpOnly; Secure; SameSite=Strict; Path=" + BrowserSession.Path;

    private static readonly string[] _cookieReaders =
    [
        nameof(SessionsController.Refresh),
        nameof(SessionsController.Logout),
    ];

    internal static IServiceCollection AddBrowserSessionDocumentation(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.PostConfigureAll<OpenApiOptions>(options =>
        {
            options.AddOperationTransformer(DescribeOperationAsync);
            options.AddSchemaTransformer(DescribeSchemaAsync);
        });

        return services;
    }

    private static Task DescribeOperationAsync(
        OpenApiOperation operation,
        OpenApiOperationTransformerContext context,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(operation);
        ArgumentNullException.ThrowIfNull(context);

        var mode = operation.Parameters?.FirstOrDefault(parameter =>
            parameter.In == ParameterLocation.Header
            && string.Equals(parameter.Name, BrowserSession.ModeHeader, StringComparison.OrdinalIgnoreCase));

        if (mode is null)
        {
            return Task.CompletedTask;
        }

        bool readsCookie = context.Description.ActionDescriptor is ControllerActionDescriptor action
            && _cookieReaders.Contains(action.ActionName, StringComparer.Ordinal);

        mode.Description = readsCookie
            ? $"Send '{BrowserSession.CookieMode}' to present the refresh token in the "
                + $"'{BrowserSession.Name}' cookie instead of the body; the cookie is read only "
                + "when this header is sent, which is what refuses a request forged from another site."
            : $"Send '{BrowserSession.CookieMode}' to receive the refresh token in the "
                + $"'{BrowserSession.Name}' cookie ({_cookieAttributes}) instead of the body, "
                + "which then omits refreshToken.";

        if (readsCookie)
        {
            operation.Parameters!.Add(new OpenApiParameter
            {
                Name = BrowserSession.Name,
                In = ParameterLocation.Cookie,
                Required = false,
                Description = $"The refresh token, read only when {BrowserSession.ModeHeader} is "
                    + $"'{BrowserSession.CookieMode}'.",
                Schema = new OpenApiSchema { Type = JsonSchemaType.String },
            });
        }

        var answers = (operation.Responses ?? [])
            .Where(response => response.Key.StartsWith('2') || (readsCookie && response.Key == "401"))
            .Select(response => response.Value)
            .OfType<OpenApiResponse>();

        foreach (var response in answers)
        {
            response.Headers ??= new Dictionary<string, IOpenApiHeader>(StringComparer.OrdinalIgnoreCase);
            response.Headers["Set-Cookie"] = new OpenApiHeader
            {
                Description = $"In cookie mode, sets or clears the '{BrowserSession.Name}' cookie "
                    + $"({_cookieAttributes}), expiring with the refresh token.",
                Schema = new OpenApiSchema { Type = JsonSchemaType.String },
            };
        }

        return Task.CompletedTask;
    }

    private static Task DescribeSchemaAsync(
        OpenApiSchema schema,
        OpenApiSchemaTransformerContext context,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(schema);
        ArgumentNullException.ThrowIfNull(context);

        if (context.JsonTypeInfo.Type != typeof(TokenResponse))
        {
            return Task.CompletedTask;
        }

        schema.Required?.Remove(_refreshTokenProperty);

        if (schema.Properties?.TryGetValue(_refreshTokenProperty, out var property) is true
            && property is OpenApiSchema refreshToken)
        {
            string absence = $"Absent in cookie mode, where the '{BrowserSession.Name}' cookie carries it.";

            refreshToken.Description = string.IsNullOrWhiteSpace(refreshToken.Description)
                ? absence
                : $"{refreshToken.Description} {absence}";
        }

        return Task.CompletedTask;
    }
}
