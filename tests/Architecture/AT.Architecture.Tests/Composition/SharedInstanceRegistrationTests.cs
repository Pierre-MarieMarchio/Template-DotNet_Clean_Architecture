using AppTemplate.Application.Core.Common.Events;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Xunit;

namespace AppTemplate.Architecture.Tests.Composition;

public sealed class SharedInstanceRegistrationTests
{
    [Fact]
    public void EveryAggregateTracker_ResolvesAsOneInstanceUnderEveryContractItServes()
    {
        var services = HostComposition.ComposeApi(HostComposition.Configuration());
        using var provider = services.BuildServiceProvider(HostComposition.StrictValidation);
        using var scope = provider.CreateScope();

        var trackers = TrackerTypes(services);

        //#if (aggregates)
        trackers.Length.ShouldBeGreaterThanOrEqualTo(
            1,
            "fewer aggregate trackers were found than this template registers, so the discovery " +
            "here has stopped reading the composition.");

        //#endif
        foreach (var tracker in trackers)
        {
            object instance = scope.ServiceProvider.GetRequiredService(tracker);
            var contracts = ContractsServedBy(services, tracker);

            contracts.Length.ShouldBeGreaterThanOrEqualTo(
                2,
                $"{tracker.Name} is reachable through fewer contracts than the guarantee needs to " +
                "be meaningful; the registration was restructured and this test needs revisiting.");

            foreach (var contract in contracts)
            {
                SameInstanceCount(scope.ServiceProvider, contract, instance)
                    .ShouldBe(
                        1,
                        $"{tracker.Name} must appear exactly once behind {contract.Name}, as the " +
                        "same object the repository fills. A second instance means the repository " +
                        "fills one identity map while the interceptor flushes another — every " +
                        "write would report success and persist nothing. Register the tracker once, and " +
                        "register each contract it serves with a factory that resolves that one " +
                        "registration, instead of registering the tracker again under the contract.");
            }
        }
    }

    [Fact]
    public void TheAggregateTracker_IsNotSharedBetweenScopes()
    {
        var services = HostComposition.ComposeApi(HostComposition.Configuration());
        using var provider = services.BuildServiceProvider(HostComposition.StrictValidation);

        var tracker = TrackerTypes(services).FirstOrDefault();
        /*#if (aggregates)*/
        tracker.ShouldNotBeNull("no aggregate tracker was found in the composition.");
        /*#else
        if (tracker is null)
        {
            return;
        }
        #endif*/

        var contract = ContractsServedBy(services, tracker).FirstOrDefault();
        contract.ShouldNotBeNull($"no contract served by {tracker.Name} was found.");

        using var first = provider.CreateScope();
        using var second = provider.CreateScope();

        object fromFirst = first.ServiceProvider.GetRequiredService(contract);
        object fromSecond = second.ServiceProvider.GetRequiredService(contract);

        ReferenceEquals(fromFirst, fromSecond).ShouldBeFalse(
            "the tracker holds a per-request identity map; sharing it across scopes would carry one " +
            "request's aggregates into another.");
    }

    [Fact]
    public void TheRule_IsSensitive_AndSeesASecondInstanceBehindAContract()
    {
        var services = new ServiceCollection();
        services.AddScoped<ProbeTracker>();
        services.AddScoped<IProbeReader>(provider => provider.GetRequiredService<ProbeTracker>());
        services.AddScoped<IProbeWriter, ProbeTracker>();
        services.AddScoped(typeof(IDomainEventConsumer), typeof(ProbeTracker));
        services.AddScoped<IProbeWriter, UnrelatedService>();

        TrackerTypes(services).ShouldBe([typeof(ProbeTracker)]);
        ContractsServedBy(services, typeof(ProbeTracker))
            .ShouldBe([typeof(ProbeTracker), typeof(IProbeReader), typeof(IProbeWriter)], ignoreOrder: true);

        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();
        object instance = scope.ServiceProvider.GetRequiredService<ProbeTracker>();

        SameInstanceCount(scope.ServiceProvider, typeof(IProbeReader), instance).ShouldBe(1);
        SameInstanceCount(scope.ServiceProvider, typeof(IProbeWriter), instance).ShouldBe(0);
    }

    private static int SameInstanceCount(IServiceProvider provider, Type contract, object instance) =>
        provider.GetServices(contract).Count(resolved => ReferenceEquals(resolved, instance));

    private interface IProbeReader;

    private interface IProbeWriter;

    private sealed class ProbeTracker : IProbeReader, IProbeWriter;

    private sealed class UnrelatedService : IProbeWriter;

    private static Type[] TrackerTypes(IServiceCollection services) =>
        [.. services
            .Select(descriptor => descriptor.ImplementationType)
            .Where(type => type is not null && type.Name.EndsWith("Tracker", StringComparison.Ordinal))
            .Distinct()
            .Cast<Type>()
            .OrderBy(type => type.Name, StringComparer.Ordinal)];

    private static Type[] ContractsServedBy(IServiceCollection services, Type tracker) =>
        [.. services
            .Where(descriptor => descriptor.ServiceType != typeof(IDomainEventConsumer))
            .Where(descriptor => descriptor.ImplementationType == tracker
                || (descriptor.ImplementationFactory is not null
                    && descriptor.ServiceType.IsAssignableFrom(tracker)))
            .Select(descriptor => descriptor.ServiceType)
            .Distinct()];
}
