using AppTemplate.Api.Common.Lifecycle;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Hosting;
using NSubstitute;
using Shouldly;
using Xunit;

namespace AppTemplate.Api.UnitTests.Common.Lifecycle;

public sealed class ShutdownHealthCheckTests
{
    /// <summary>
    /// <see cref="ShutdownHealthCheck"/> is the first <c>IHealthCheck</c> in this codebase that
    /// takes a constructor dependency, so it is worth proving — not just assuming — that
    /// <c>AddCheck&lt;T&gt;</c> (exactly how <c>Program.cs</c> registers it) resolves that
    /// dependency through DI rather than requiring a parameterless constructor.
    /// </summary>
    [Fact]
    public async Task RegisteredViaAddCheckOfT_ResolvesItsConstructorDependencyThroughDI()
    {
        using var shutdownSignal = new CancellationTokenSource();
        shutdownSignal.Cancel();
        var lifetime = Substitute.For<IHostApplicationLifetime>();
        lifetime.ApplicationStopping.Returns(shutdownSignal.Token);

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(lifetime);
        services.AddHealthChecks().AddCheck<ShutdownHealthCheck>(name: "shutdown", tags: ["ready"]);
        using var provider = services.BuildServiceProvider();

        var report = await provider.GetRequiredService<HealthCheckService>()
            .CheckHealthAsync(TestContext.Current.CancellationToken);

        report.Entries["shutdown"].Status.ShouldBe(HealthStatus.Unhealthy);
    }

    [Fact]
    public async Task CheckHealthAsync_IsHealthy_BeforeShutdownBegins()
    {
        using var shutdownSignal = new CancellationTokenSource();
        var lifetime = Substitute.For<IHostApplicationLifetime>();
        lifetime.ApplicationStopping.Returns(shutdownSignal.Token);
        var check = new ShutdownHealthCheck(lifetime);

        HealthCheckResult result = await check.CheckHealthAsync(new HealthCheckContext(), TestContext.Current.CancellationToken);

        result.Status.ShouldBe(HealthStatus.Healthy);
    }

    [Fact]
    public async Task CheckHealthAsync_IsUnhealthy_OnceShutdownHasBegun()
    {
        using var shutdownSignal = new CancellationTokenSource();
        shutdownSignal.Cancel();
        var lifetime = Substitute.For<IHostApplicationLifetime>();
        lifetime.ApplicationStopping.Returns(shutdownSignal.Token);
        var check = new ShutdownHealthCheck(lifetime);

        HealthCheckResult result = await check.CheckHealthAsync(new HealthCheckContext(), TestContext.Current.CancellationToken);

        result.Status.ShouldBe(HealthStatus.Unhealthy);
    }
}
