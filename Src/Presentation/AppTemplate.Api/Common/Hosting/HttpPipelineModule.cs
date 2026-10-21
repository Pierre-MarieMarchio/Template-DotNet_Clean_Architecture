using AppTemplate.Api.Common.Caching;
using AppTemplate.Api.Common.Concurrency;
using AppTemplate.Api.Common.Errors;
using AppTemplate.Api.Common.Idempotency;
using AppTemplate.Api.Common.Localization;
using AppTemplate.Api.Common.Observability;
using AppTemplate.Api.Common.OpenApi;
using AppTemplate.Api.Common.Security;
using Asp.Versioning;
using Asp.Versioning.ApiExplorer;
using Microsoft.AspNetCore.OpenApi;

namespace AppTemplate.Api.Common.Hosting;

public static class HttpPipelineModule
{
    public static IServiceCollection AddHttpPipeline(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        services.AddApiCallerIdentity();

        services.AddApiModelStateProblemDetails();
        services.AddApiProblemDetails();

        services.AddRequestLanguage();

        services.AddApiForwardedHeaders(configuration);
        services.AddApiSecurityHeaders(configuration);
        services.AddApiCors(configuration);
        services.AddApiRateLimiting(configuration);
        services.AddApiConcurrency(configuration);
        services.AddApiIdempotency(configuration);
        services.AddApiRequestLimits(configuration);
        services.AddApiLifecycle(configuration);

        return services;
    }

    public static IServiceCollection AddUrlSegmentApiVersioning(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddApiVersioning(options =>
            {
                options.DefaultApiVersion = new ApiVersion(1, 0);
                options.ApiVersionReader = new UrlSegmentApiVersionReader();
                options.ReportApiVersions = true;
            })
            .AddMvc()
            .AddApiExplorer(options =>
            {
                options.GroupNameFormat = "'v'VVV";
                options.SubstituteApiVersionInUrl = true;
            });

        return services;
    }

    public static IReadOnlyList<string> ApiVersionGroups(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

#pragma warning disable ASP0000
        using var versionProvider = services.BuildServiceProvider();
#pragma warning restore ASP0000

        return
        [
            .. versionProvider
                .GetRequiredService<IApiVersionDescriptionProvider>()
                .ApiVersionDescriptions
                .Select(description => description.GroupName),
        ];
    }

    public static void UseApiDocumentConventions(this OpenApiOptions options, string groupName)
    {
        ArgumentNullException.ThrowIfNull(options);

        //#if (!publicApi)
        options.AddDocumentTransformer<OpenApiSecurityTransformer>();
        //#endif
        options.AddSchemaTransformer<OpenApiDiscriminatorTransformer>();
        options.ShouldInclude = apiDescription => apiDescription.GroupName == groupName;
    }

    public static WebApplication UseHttpPipeline(
        this WebApplication app,
        ResponseContentSecurityPolicy? apiReferencePolicy = null)
    {
        ArgumentNullException.ThrowIfNull(app);

        app.UseApiForwardedHeaders();
        app.UseApiSecurityHeaders(apiReferencePolicy);
        app.UseApiCacheHeaders();
        app.UseApiRequestLogging();
        app.UseApiRequestLimits();

        app.UseExceptionHandler();
        app.UseStatusCodePages();

        app.UseCors(CorsExtensions.Default);
        app.UseRateLimiter();

        app.UseRequestLanguage();
        app.UseApiRequestTimeouts();

        return app;
    }
}
