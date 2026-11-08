using System.Globalization;
using System.Reflection;
using AppTemplate.Api.Common.Caching;
using AppTemplate.Api.Common.Concurrency;
using AppTemplate.Api.Common.Errors;
using AppTemplate.Api.Common.Idempotency;
using AppTemplate.Api.Common.Localization;
using AppTemplate.Api.Common.Observability;
using AppTemplate.Api.Common.OpenApi;
using AppTemplate.Api.Common.Security;
using Asp.Versioning;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Mvc.Infrastructure;
using Microsoft.AspNetCore.OpenApi;

namespace AppTemplate.Api.Common.Hosting;

public static class HttpPipelineModule
{
    private const string _versionGroupFormat = "'v'VVV";

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
                options.ApiVersionReader = new UrlSegmentApiVersionReader();
                options.ReportApiVersions = true;
            })
            .AddMvc()
            .AddApiExplorer(options =>
            {
                options.GroupNameFormat = _versionGroupFormat;
                options.SubstituteApiVersionInUrl = true;
            });

        return services;
    }

    public static IReadOnlyList<string> ApiVersionGroups(this IMvcBuilder controllers)
    {
        ArgumentNullException.ThrowIfNull(controllers);

        var feature = new ControllerFeature();
        controllers.PartManager.PopulateFeature(feature);

        ApiVersion[] versions = [.. feature.Controllers.SelectMany(DeclaredApiVersions).Distinct().Order()];

        return
        [
            .. (versions.Length == 0 ? [ApiVersion.Default] : versions)
                .Select(version => version.ToString(_versionGroupFormat, CultureInfo.InvariantCulture)),
        ];
    }

    public static void UseApiDocumentConventions(this OpenApiOptions options, string groupName)
    {
        ArgumentNullException.ThrowIfNull(options);

        options.AddSchemaTransformer<OpenApiProblemSchemaTransformer>();
        options.AddOperationTransformer<OpenApiProblemMediaTypeTransformer>();
        options.AddDocumentTransformer<OpenApiValidationProblemTransformer>();
        //#if (!publicApi)
        options.AddDocumentTransformer<OpenApiSecurityTransformer>();
        options.AddSchemaTransformer<OpenApiDiscriminatorTransformer>();
        //#endif
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

    private static IEnumerable<ApiVersion> DeclaredApiVersions(TypeInfo controller)
    {
        object[] attributes = controller.GetCustomAttributes(inherit: true);

        ApiVersion[] declared =
        [
            .. attributes
                .Concat(controller.GetMethods().SelectMany(action => action.GetCustomAttributes(inherit: true)))
                .OfType<IApiVersionProvider>()
                .Where(provider => !provider.Options.HasFlag(ApiVersionProviderOptions.Advertised))
                .SelectMany(provider => provider.Versions),
        ];

        bool implicitlyVersioned = declared.Length == 0
            && !attributes.OfType<IApiVersionNeutral>().Any()
            && attributes.Concat(controller.Assembly.GetCustomAttributes()).OfType<IApiBehaviorMetadata>().Any();

        return implicitlyVersioned ? [ApiVersion.Default] : declared;
    }
}
