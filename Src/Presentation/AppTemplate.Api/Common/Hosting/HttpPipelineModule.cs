using AppTemplate.Api.Common.Caching;
using AppTemplate.Api.Common.Concurrency;
using AppTemplate.Api.Common.Errors;
using AppTemplate.Api.Common.Idempotency;
using AppTemplate.Api.Common.Localization;
using AppTemplate.Api.Common.Observability;
//#if (!publicApi)
using AppTemplate.Api.Common.OpenApi;
//#endif
using AppTemplate.Api.Common.Security;
using Asp.Versioning;
using Asp.Versioning.ApiExplorer;
using Microsoft.AspNetCore.OpenApi;

namespace AppTemplate.Api.Common.Hosting;

/// <summary>One call to compose the HTTP mechanisms this API configures, and one to install them in the order they have to run in.</summary>
/// <remarks>
/// Telemetry is deliberately absent from <see cref="AddHttpPipeline"/>: it needs this assembly for
/// <c>service.name</c> and the host's own database instrumentation, so the host calls
/// <see cref="Observability.ObservabilityExtensions.AddApiObservability"/> itself.
/// </remarks>
public static class HttpPipelineModule
{
    /// <summary>Registers the eight cross-cutting mechanisms configured the same way in any HTTP
    /// host — forwarded headers, security headers, CORS, rate limiting, concurrency, idempotency,
    /// request limits and lifecycle — plus RFC 7807 problem details for application and MVC
    /// failures, the request language, and the current caller.</summary>
    /// <param name="services">The container being built.</param>
    /// <param name="configuration">Where every options section is bound from.</param>
    /// <returns>The same collection, for chaining.</returns>
    public static IServiceCollection AddHttpPipeline(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        services.AddApiCallerIdentity();

        // Model binding must answer the same ProblemDetails shape as an application validation
        // failure, so these two are registered together and before anything can bind a request.
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

    /// <summary>URL-segment versioning, with the reader named rather than left to the default
    /// composite, which also inspects a query string and a header this template never populates.</summary>
    /// <param name="services">The container being built.</param>
    /// <returns>The same collection, for chaining.</returns>
    /// <remarks>Not <c>AssumeDefaultVersionWhenUnspecified</c>: every route template carries
    /// <c>api/v{version:apiVersion}</c>, so a request naming no version never reaches routing and
    /// the option would have nothing to apply to.</remarks>
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

    /// <summary>The API version groups the OpenAPI documents are produced one per, in the order the
    /// version provider reports them.</summary>
    /// <param name="services">The container being built, after <see cref="AddUrlSegmentApiVersioning"/>.</param>
    /// <returns>One group name per version, such as <c>v1</c>.</returns>
    /// <remarks>Nothing is resolved from the throwaway provider except the version list, the only
    /// supported way to read it before the real host exists — the only point <c>AddOpenApi</c> can
    /// still be called. Hence the suppression.</remarks>
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

    /// <summary>
    /// What every document of this template declares: the bearer scheme when the API authenticates,
    /// and the one version group it describes.
    /// </summary>
    /// <param name="options">The document being configured.</param>
    /// <param name="groupName">The group this document is for, from <see cref="ApiVersionGroups"/>.</param>
    /// <remarks>Restricting the document to its own group is the point of the pair: a single
    /// <c>AddOpenApi()</c> captures every action regardless of group, so a v2 added later would show up
    /// inside the v1 document too. <b>Why <c>Program.cs</c> makes the <c>AddOpenApi</c> call itself:</b>
    /// the XML documentation that becomes a schema's <c>description</c> is collected by a source
    /// generator hooked to that call through an interceptor, so the comments it sees are the ones in
    /// the assembly holding the call — moved into a referenced project, every request and response
    /// contract in the document would lose its description without anything failing to compile.</remarks>
    public static void UseApiDocumentConventions(this OpenApiOptions options, string groupName)
    {
        ArgumentNullException.ThrowIfNull(options);

        //#if (!publicApi)
        options.AddDocumentTransformer<OpenApiSecurityTransformer>();
        //#endif
        options.ShouldInclude = apiDescription => apiDescription.GroupName == groupName;
    }

    /// <summary>The request pipeline, in the one order that works. Call it before authentication,
    /// authorization and endpoint mapping, which stay with the host.</summary>
    /// <param name="app">The application being configured.</param>
    /// <param name="apiReferencePolicy">How one path answers under a content-security policy other
    /// than the configured one, or <see langword="null"/> when every response takes the configured
    /// one; a host serving an API-reference page supplies it in that branch.</param>
    /// <returns>The same application, for chaining.</returns>
    /// <remarks>Deliberately one call rather than a step at a time: six of these orderings are
    /// coupled pairwise, so an extension point in the middle would offer a caller exactly the
    /// arrangement this method exists to prevent. The order and its reasons are in
    /// <c>docs/DECISIONS.md</c>.</remarks>
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
