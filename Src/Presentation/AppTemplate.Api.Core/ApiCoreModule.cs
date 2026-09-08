using AppTemplate.Api.Core.Common.Caching;
using AppTemplate.Api.Core.Common.Concurrency;
using AppTemplate.Api.Core.Common.Errors;
using AppTemplate.Api.Core.Common.Hosting;
using AppTemplate.Api.Core.Common.Idempotency;
using AppTemplate.Api.Core.Common.Localization;
using AppTemplate.Api.Core.Common.Observability;
using AppTemplate.Api.Core.Common.OpenApi;
using AppTemplate.Api.Core.Common.Security;
using Asp.Versioning;
using Asp.Versioning.ApiExplorer;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace AppTemplate.Api.Core;

/// <summary>
/// One call to compose what every HTTP host of this template needs, and one to install it in the
/// order it has to run in.
/// </summary>
/// <remarks>
/// Telemetry is deliberately absent from <see cref="AddApiCore"/>: it needs the host's own assembly
/// for <c>service.name</c> and the host's own database instrumentation, neither of which a shared
/// project can supply. The host calls
/// <see cref="Common.Observability.ObservabilityExtensions.AddCoreObservability"/> itself, through a
/// thin extension of its own.
/// </remarks>
public static class ApiCoreModule
{
    /// <summary>
    /// Registers the eight cross-cutting mechanisms that are configured the same way in any HTTP
    /// host: forwarded headers, security headers, CORS, rate limiting, concurrency, idempotency,
    /// request limits and lifecycle — plus RFC 7807 problem details for both application failures
    /// and the ones MVC produces itself, the request language, and the current caller.
    /// </summary>
    /// <param name="services">The container being built.</param>
    /// <param name="configuration">Where every options section is bound from.</param>
    /// <returns>The same collection, for chaining.</returns>
    public static IServiceCollection AddApiCore(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        services.AddApiCurrentUser();

        // Model binding must answer the same ProblemDetails shape as an application validation
        // failure, so these two are registered together and before anything can bind a request.
        services.AddApiModelStateProblemDetails();
        services.AddApiProblemDetails();

        services.AddRequestLanguage();

        services.AddApiForwardedHeaders(configuration);
        services.AddApiSecurityHeaders(configuration);
        services.AddApiCors(configuration);
        services.AddApiRateLimiting();
        services.AddApiConcurrency(configuration);
        services.AddApiIdempotency(configuration);
        services.AddApiRequestLimits(configuration);
        services.AddApiLifecycle(configuration);

        return services;
    }

    /// <summary>
    /// URL-segment versioning, with the reader named rather than left to the default composite —
    /// which also inspects a query string and a header this template never populates.
    /// </summary>
    /// <param name="services">The container being built.</param>
    /// <returns>The same collection, for chaining.</returns>
    /// <remarks>
    /// Not <c>AssumeDefaultVersionWhenUnspecified</c>: every route template here carries
    /// <c>api/v{version:apiVersion}</c>, which makes the segment mandatory, so a request naming no
    /// version never reaches routing at all and the option would have nothing to apply to.
    /// </remarks>
    public static IServiceCollection AddCoreApiVersioning(this IServiceCollection services)
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

    /// <summary>
    /// The API version groups the OpenAPI documents are produced one per, in the order the version
    /// provider reports them.
    /// </summary>
    /// <param name="services">The container being built, after <see cref="AddCoreApiVersioning"/>.</param>
    /// <returns>One group name per version, such as <c>v1</c>.</returns>
    /// <remarks>
    /// Nothing is resolved from the throwaway provider except the version list itself, and there is
    /// no other supported way to read that list before the real host exists — which is the only
    /// point <c>AddOpenApi</c> can still be called. Hence the suppression, which is about the extra
    /// singleton copy that reasoning rules out.
    /// </remarks>
    public static IReadOnlyList<string> CoreApiVersionGroups(this IServiceCollection services)
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
    /// What every document of this template declares: the bearer scheme, and the one version group
    /// it describes.
    /// </summary>
    /// <param name="options">The document being configured.</param>
    /// <param name="groupName">The group this document is for, from <see cref="CoreApiVersionGroups"/>.</param>
    /// <remarks>
    /// <para>
    /// Restricting the document to its own group is the point of the pair. A single
    /// <c>AddOpenApi()</c> captures every action regardless of group, so a v2 added later would show
    /// up inside the v1 document too.
    /// </para>
    /// <para>
    /// <b>Why the host makes the <c>AddOpenApi</c> call itself rather than this project making it.</b>
    /// The XML documentation that becomes a schema's <c>description</c> is collected by a source
    /// generator that hooks the <c>AddOpenApi</c> call through an interceptor, so the comments it can
    /// see are the ones in the assembly holding that call. Made from here, the call would carry this
    /// project's comments and none of the host's, and every request and response contract in the
    /// document would lose its description without anything failing to compile.
    /// </para>
    /// </remarks>
    public static void UseCoreDocumentConventions(this OpenApiOptions options, string groupName)
    {
        ArgumentNullException.ThrowIfNull(options);

        options.AddDocumentTransformer<OpenApiSecurityTransformer>();
        options.ShouldInclude = apiDescription => apiDescription.GroupName == groupName;
    }

    /// <summary>
    /// The request pipeline, in the one order that works. Call it before authentication,
    /// authorization and endpoint mapping, which stay with the host.
    /// </summary>
    /// <param name="app">The application being configured.</param>
    /// <param name="apiReferencePolicy">
    /// How one path answers under a content-security policy other than the configured one, or
    /// <see langword="null"/> when every response takes the configured one. A host serving an
    /// API-reference page supplies it in the branch where it mounts that page.
    /// </param>
    /// <returns>The same application, for chaining.</returns>
    /// <remarks>
    /// Deliberately one call rather than a step at a time: six of these orderings are coupled
    /// pairwise, so an extension point in the middle would offer a caller exactly the arrangement
    /// this method exists to prevent.
    /// <list type="bullet">
    /// <item>Forwarded headers first, before anything reads the client address or the scheme: the
    /// rate limiter partitions on the remote address, and CORS, authentication and the exception
    /// handler all observe the scheme.</item>
    /// <item>Security headers and cache headers next, so that a response written by the exception
    /// handler, the rate limiter or a health endpoint carries the same headers as one written by a
    /// controller. Both write from <c>OnStarting</c>, which is what survives the
    /// <c>Response.Clear()</c> that <c>UseExceptionHandler</c> performs.</item>
    /// <item>The request log outside the exception handler, so the entry reports the status the
    /// caller received, and ahead of the size limit, which answers 413 without calling the next
    /// middleware.</item>
    /// <item>Request limits before anything reads the body, so an oversized request is refused
    /// rather than buffered.</item>
    /// <item>The language and the timeout after the rate limiter, so a rejected request neither
    /// does work nor starts a clock that then has to be torn down.</item>
    /// <item>The timeout before authentication and authorization, so the deadline covers them too
    /// and not just the action.</item>
    /// </list>
    /// HTTPS redirection is not installed and HSTS is not sent: TLS terminates upstream, so a
    /// redirect would answer the orchestrator's probe with a 307, and <c>max-age</c>,
    /// <c>includeSubDomains</c> and <c>preload</c> are commitments over a whole domain that this
    /// application cannot know. The component terminating TLS is the one that knows them.
    /// </remarks>
    public static WebApplication UseCorePipeline(
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
