using System.Net;
using Aspire.Hosting.ApplicationModel;
using Aspire.Hosting.Testing;
using Shouldly;
using Xunit;

namespace AppTemplate.AppHost.IntegrationTests;

public sealed class LocalStackTests
{
    private static readonly TimeSpan _startLimit = TimeSpan.FromMinutes(5);

    [Fact]
    public async Task EveryResourceOfTheStack_StartsAndPassesItsHealthChecks_AndTheApiAnswersItsHealthEndpoint()
    {
        using var limit = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        limit.CancelAfter(_startLimit);

        var builder = await DistributedApplicationTestingBuilder.CreateAsync(
            typeof(AppHostModule),
            [.. Secrets()],
            static (options, _) => options.DisableDashboard = true,
            limit.Token);

        await using var app = await builder.BuildAsync(limit.Token);
        await app.StartAsync(limit.Token);

        var started = builder.Resources.Where(static resource => resource is ProjectResource or ContainerResource).ToList();
        started.ShouldNotBeEmpty("the app host declares the API or the worker in every shape; a stack of nothing is broken.");

        foreach (var resource in started)
        {
            if (resource.Annotations.OfType<HealthCheckAnnotation>().Any())
            {
                await app.ResourceNotifications.WaitForResourceHealthyAsync(resource.Name, limit.Token);
            }
            else
            {
                await app.ResourceNotifications.WaitForResourceAsync(resource.Name, KnownResourceStates.Running, limit.Token);
            }
        }
        //#if (api)

        using var client = app.CreateHttpClient("api", "http");
        using var response = await client.GetAsync(new Uri("/health", UriKind.Relative), limit.Token);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        //#endif
    }

    private static IEnumerable<string> Secrets()
    {
        string[] secrets =
        [
            "db-password",
            //#if (files)
            "storage-secret-access-key",
            //#endif
            //#if (localAuth)
            "jwt-key",
            "seed-admin-password",
            //#endif
        ];

        foreach (string secret in secrets)
        {
            yield return $"--Parameters:{secret}=Aa1-{Guid.NewGuid():N}{Guid.NewGuid():N}";
        }
        /*#if (externalAuth)

        yield return "--Parameters:external-auth-authority=https://identity.example.com/";
        yield return "--Parameters:external-auth-audience=api";
        #endif*/
    }
}
