using AppTemplate.Application.Common.Abstractions;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Xunit;

namespace AppTemplate.Architecture.Tests.Composition;

/// <summary>
/// Guards the types that must resolve as ONE instance under several contracts.
/// </summary>
/// <remarks>
/// The aggregate tracker is reached three ways: the repository fills its identity map through one
/// contract, the flush interceptor drains it through another, and the dispatcher takes domain events
/// off it through a third. Registered as three independent descriptors it would resolve as three
/// objects, the repository would fill one map and the interceptor would flush a different empty one,
/// and **every write would report success while persisting nothing**. Nothing else catches that: the
/// graph resolves, the build is clean, and only an assertion on reference identity fails.
/// </remarks>
public sealed class SharedInstanceRegistrationTests
{
    /// <summary>
    /// Turns red if any of the three registrations stops delegating to the same descriptor — for
    /// example by becoming its own <c>AddScoped&lt;IAggregateFlusher, TodoListTracker&gt;()</c>.
    /// </summary>
    [Fact]
    public void TheAggregateTracker_ResolvesAsOneInstanceUnderEveryContractItServes()
    {
        var services = HostComposition.ComposeApi(HostComposition.Configuration());
        using var provider = services.BuildServiceProvider(HostComposition.StrictValidation);
        using var scope = provider.CreateScope();

        var contracts = TrackerContracts(services).ToArray();

        contracts.Length.ShouldBeGreaterThanOrEqualTo(
            2,
            "the guarantee is meaningless unless the tracker is reachable through several contracts; " +
            "if this fails, the registration was restructured and this test needs revisiting.");

        var resolved = contracts
            .Select(contract => scope.ServiceProvider.GetRequiredService(contract))
            .ToArray();

        foreach (var instance in resolved)
        {
            ReferenceEquals(instance, resolved[0]).ShouldBeTrue(
                $"every contract the tracker serves must resolve to the same object; " +
                $"{string.Join(", ", contracts.Select(contract => contract.Name))} did not. " +
                "Register it once and have the other contracts delegate to that registration.");
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

        var contract = TrackerContracts(services).FirstOrDefault();
        contract.ShouldNotBeNull("no contract served by the aggregate tracker was found.");

        using var first = provider.CreateScope();
        using var second = provider.CreateScope();

        var fromFirst = first.ServiceProvider.GetRequiredService(contract);
        var fromSecond = second.ServiceProvider.GetRequiredService(contract);

        ReferenceEquals(fromFirst, fromSecond).ShouldBeFalse(
            "the tracker holds a per-request identity map; sharing it across scopes would carry one " +
            "request's aggregates into another.");
    }

    /// <summary>
    /// Every contract whose implementation type is the aggregate tracker, discovered from the
    /// descriptors rather than hard-coded, so a tracker added for a second aggregate is covered too.
    /// </summary>
    private static IEnumerable<Type> TrackerContracts(IServiceCollection services)
    {
        var trackerTypes = services
            .Select(descriptor => descriptor.ImplementationType)
            .Where(type => type is not null && type.Name.EndsWith("Tracker", StringComparison.Ordinal))
            .Distinct()
            .ToArray();

        return services
            .Where(descriptor => descriptor.ServiceType != typeof(IDomainEventConsumer)
                && (Array.IndexOf(trackerTypes, descriptor.ImplementationType) >= 0
                    || IsFactoryForATracker(descriptor, trackerTypes)))
            .Select(descriptor => descriptor.ServiceType)
            .Distinct();
    }

    /// <summary>
    /// A delegating registration carries no <c>ImplementationType</c>, so it is recognised by the
    /// contracts the tracker's own type declares.
    /// </summary>
    private static bool IsFactoryForATracker(ServiceDescriptor descriptor, Type?[] trackerTypes)
    {
        if (descriptor.ImplementationFactory is null)
        {
            return false;
        }

        return trackerTypes.Any(tracker =>
            tracker is not null && descriptor.ServiceType.IsAssignableFrom(tracker));
    }
}
