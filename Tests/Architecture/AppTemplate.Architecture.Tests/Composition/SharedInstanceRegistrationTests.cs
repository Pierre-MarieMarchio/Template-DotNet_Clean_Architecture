using AppTemplate.Application.Common.Events;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Xunit;

namespace AppTemplate.Architecture.Tests.Composition;

/// <summary>
/// Guards the types that must resolve as ONE instance under several contracts.
/// </summary>
/// <remarks>
/// An aggregate tracker is reached three ways: the repository fills its identity map through one
/// contract, the flush interceptor drains it through another, and the dispatcher takes domain events
/// off it through a third. Registered as three independent descriptors it would resolve as three
/// objects, the repository would fill one map and the interceptor would flush a different empty one,
/// and **every write would report success while persisting nothing**. Nothing else catches that: the
/// graph resolves, the build is clean, and only an assertion on reference identity fails.
/// </remarks>
public sealed class SharedInstanceRegistrationTests
{
    /// <summary>
    /// Turns red if any of a tracker's registrations stops delegating to the same descriptor — for
    /// example by becoming its own <c>AddScoped&lt;IAggregateFlusher, TodoListTracker&gt;()</c>.
    /// <para>
    /// Asserted per tracker, not across all of them: each aggregate has its own, and the contracts
    /// they share are collection contracts — the flush interceptor takes
    /// <c>IEnumerable&lt;IAggregateFlusher&gt;</c> precisely so that every aggregate's map is
    /// drained. What must hold is that a given tracker appears in those collections **as the same
    /// object** the repository filled, once each.
    /// </para>
    /// </summary>
    [Fact]
    public void EveryAggregateTracker_ResolvesAsOneInstanceUnderEveryContractItServes()
    {
        var services = HostComposition.ComposeApi(HostComposition.Configuration());
        using var provider = services.BuildServiceProvider(HostComposition.StrictValidation);
        using var scope = provider.CreateScope();

        var trackers = TrackerTypes(services);

        trackers.Length.ShouldBeGreaterThanOrEqualTo(
            2,
            "fewer aggregate trackers were found than this template registers, so the discovery " +
            "here has stopped reading the composition.");

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
                scope.ServiceProvider
                    .GetServices(contract)
                    .Count(resolved => ReferenceEquals(resolved, instance))
                    .ShouldBe(
                        1,
                        $"{tracker.Name} must appear exactly once behind {contract.Name}, as the " +
                        "same object the repository fills. A second instance means the repository " +
                        "fills one identity map while the interceptor flushes another — every " +
                        "write would report success and persist nothing.");
            }
        }
    }

    /// <summary>
    /// The other half of the invariant: one instance per scope, never shared between scopes. A
    /// singleton tracker would leak one request's identity map into the next.
    /// </summary>
    [Fact]
    public void TheAggregateTracker_IsNotSharedBetweenScopes()
    {
        var services = HostComposition.ComposeApi(HostComposition.Configuration());
        using var provider = services.BuildServiceProvider(HostComposition.StrictValidation);

        var tracker = TrackerTypes(services).FirstOrDefault();
        tracker.ShouldNotBeNull("no aggregate tracker was found in the composition.");

        var contract = ContractsServedBy(services, tracker).FirstOrDefault();
        contract.ShouldNotBeNull($"no contract served by {tracker.Name} was found.");

        using var first = provider.CreateScope();
        using var second = provider.CreateScope();

        var fromFirst = first.ServiceProvider.GetRequiredService(contract);
        var fromSecond = second.ServiceProvider.GetRequiredService(contract);

        ReferenceEquals(fromFirst, fromSecond).ShouldBeFalse(
            "the tracker holds a per-request identity map; sharing it across scopes would carry one " +
            "request's aggregates into another.");
    }

    /// <summary>
    /// Every aggregate tracker, discovered from the descriptors rather than hard-coded, so one
    /// added for a further aggregate is covered without this test being touched.
    /// </summary>
    private static Type[] TrackerTypes(IServiceCollection services) =>
        [.. services
            .Select(descriptor => descriptor.ImplementationType)
            .Where(type => type is not null && type.Name.EndsWith("Tracker", StringComparison.Ordinal))
            .Distinct()
            .Cast<Type>()
            .OrderBy(type => type.Name, StringComparer.Ordinal)];

    /// <summary>
    /// The contracts one tracker answers to. A delegating registration carries no
    /// <c>ImplementationType</c>, so it is recognised by what the tracker's own type can satisfy.
    /// </summary>
    private static Type[] ContractsServedBy(IServiceCollection services, Type tracker) =>
        [.. services
            .Where(descriptor => descriptor.ServiceType != typeof(IDomainEventConsumer))
            .Where(descriptor => descriptor.ImplementationType == tracker
                || (descriptor.ImplementationFactory is not null
                    && descriptor.ServiceType.IsAssignableFrom(tracker)))
            .Select(descriptor => descriptor.ServiceType)
            .Distinct()];
}
