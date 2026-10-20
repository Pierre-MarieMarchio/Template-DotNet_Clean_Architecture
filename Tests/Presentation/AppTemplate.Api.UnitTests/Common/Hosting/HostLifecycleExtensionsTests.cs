using AppTemplate.Api.Common.Hosting;
using Microsoft.AspNetCore.Http.Timeouts;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Shouldly;
using Xunit;

namespace AppTemplate.Api.UnitTests.Common.Hosting;

public sealed class HostLifecycleExtensionsTests
{
    [Fact]
    public void AddApiLifecycle_AppliesTheBoundShutdownTimeout_ToHostOptions()
    {
        var provider = BuildProvider(shutdownTimeout: "00:01:15");

        var hostOptions = provider.GetRequiredService<IOptions<HostOptions>>().Value;

        hostOptions.ShutdownTimeout.ShouldBe(TimeSpan.FromSeconds(75));
    }

    [Fact]
    public void AddApiLifecycle_ConfiguresTheDefaultRequestTimeoutPolicy()
    {
        var provider = BuildProvider(requestTimeoutDefault: "00:02:00");

        var requestTimeouts = provider.GetRequiredService<IOptions<RequestTimeoutOptions>>().Value;

        requestTimeouts.DefaultPolicy.ShouldNotBeNull();
        requestTimeouts.DefaultPolicy!.Timeout.ShouldBe(TimeSpan.FromMinutes(2));
    }

    private static ServiceProvider BuildProvider(
        string shutdownTimeout = "00:00:30",
        string requestTimeoutDefault = "00:05:00")
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Shutdown:Timeout"] = shutdownTimeout,
                ["RequestTimeouts:Default"] = requestTimeoutDefault,
            })
            .Build();

        var services = new ServiceCollection();
        services.AddApiLifecycle(configuration);

        return services.BuildServiceProvider();
    }
}
