//#if (!publicApi)
using System.Net;
//#endif
using Microsoft.AspNetCore.Authorization;
//#if (!publicApi)
using Microsoft.AspNetCore.Authorization.Infrastructure;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.Routing;
//#endif
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Xunit;

namespace AppTemplate.PresentationAppPortal.UnitTests.Common.Security;

public sealed class DefaultDenyTests
{
    //#if (!publicApi)
    private static readonly HttpStatusCode[] _challenges =
        [HttpStatusCode.Redirect, HttpStatusCode.Unauthorized, HttpStatusCode.Forbidden];

    [Fact]
    public async Task TheFallbackPolicy_RequiresASignedInUser()
    {
        using var host = new PresentationHost();
        using var scope = host.Services.CreateScope();

        var fallback = await scope.ServiceProvider.GetRequiredService<IAuthorizationPolicyProvider>()
            .GetFallbackPolicyAsync();

        fallback.ShouldNotBeNull();
        fallback.Requirements.OfType<DenyAnonymousAuthorizationRequirement>().ShouldNotBeEmpty();
    }

    [Fact]
    public async Task EveryEndpointNotMarkedAnonymous_ChallengesACallerWhoIsNotSignedIn()
    {
        using var host = new PresentationHost();
        using var client = host.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

        var routes = ProtectedRoutes(host);

        routes.ShouldNotBeEmpty("No endpoint was found to probe, so nothing below is checked.");

        var offenders = new List<string>();

        foreach (string route in routes)
        {
            using var response = await client.GetAsync(new Uri(route, UriKind.Relative), TestContext.Current.CancellationToken);

            if (!_challenges.Contains(response.StatusCode))
            {
                offenders.Add($"GET {route}: {(int)response.StatusCode}");
            }
        }

        offenders.ShouldBeEmpty(
            "The portal is default-deny: a page or endpoint that does not opt out answers a caller " +
            "who is not signed in with a challenge, never with its content.");
    }

    [Fact]
    public async Task AChallengedResponse_SendsTheBrowserToTheSignInRoute_AndCarriesTheSecurityHeaders()
    {
        using var host = new PresentationHost();
        using var client = host.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

        using var response = await client.GetAsync(
            new Uri(ProtectedRoutes(host)[0], UriKind.Relative),
            TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.Redirect);
        response.Headers.Location!.AbsolutePath.ShouldBe("/account/sign-in");
        SecurityHeaderTests.Header(response, "X-Frame-Options").ShouldBe("DENY");
        SecurityHeaderTests.Header(response, "Content-Security-Policy").ShouldContain("frame-ancestors 'none'");
    }

    [Fact]
    public async Task TheLivenessProbe_AnswersACallerWhoIsNotSignedIn()
    {
        using var host = new PresentationHost();
        using var client = host.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

        using var response = await client.GetAsync(new Uri("/health", UriKind.Relative), TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    private static List<string> ProtectedRoutes(PresentationHost host) =>
        [.. host.Services.GetRequiredService<EndpointDataSource>().Endpoints
            .OfType<RouteEndpoint>()
            .Where(endpoint => endpoint.Metadata.GetMetadata<IAllowAnonymous>() is null
                && endpoint.Metadata.GetMetadata<IAuthorizeData>() is null
                && (endpoint.Metadata.GetMetadata<IHttpMethodMetadata>()?.HttpMethods.Contains("GET") ?? true))
            .Select(endpoint => endpoint.RoutePattern.RawText ?? string.Empty)
            .Where(route => route.StartsWith('/') && !route.Contains('{', StringComparison.Ordinal))
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)];
    //#else
    [Fact]
    public async Task ThePortal_AuthenticatesNobody_SoItSetsNoFallbackPolicy()
    {
        using var host = new PresentationHost();
        using var scope = host.Services.CreateScope();

        var fallback = await scope.ServiceProvider.GetRequiredService<IAuthorizationPolicyProvider>()
            .GetFallbackPolicyAsync();

        fallback.ShouldBeNull();
    }
    //#endif
}
