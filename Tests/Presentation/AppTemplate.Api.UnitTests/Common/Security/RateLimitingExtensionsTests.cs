using System.Net;
using System.Threading.RateLimiting;
using AppTemplate.Api.Common.Security;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using Shouldly;
using Xunit;

namespace AppTemplate.Api.UnitTests.Common.Security;

/// <summary>
/// What <c>AddApiRateLimiting</c> composes, read from the options the middleware would be handed.
/// </summary>
/// <remarks>
/// <para>
/// <c>RateLimiterOptions</c> keeps its named policies in an internal map, so the <c>authentication</c>
/// policy cannot be invoked from here — but both policies ask the counters for their partitioner while
/// the host is composed, and that is observable, so the budget that policy was built with is provable
/// without a live pipeline. What it does with the partitioner afterwards is the integration suite's.
/// </para>
/// <para>
/// The double below is written by hand rather than substituted: <c>IRateLimitCounters</c> is internal,
/// and Castle cannot proxy an internal interface unless the assembly grants its internals to
/// <c>DynamicProxyGenAssembly2</c> as well — a wider grant than one test is worth.
/// </para>
/// </remarks>
public sealed class RateLimitingExtensionsTests
{
    [Fact]
    public void AddApiRateLimiting_InstallsTheInProcessCounters()
    {
        using var provider = new ServiceCollection().AddApiRateLimiting().BuildServiceProvider();

        provider.GetRequiredService<IRateLimitCounters>().ShouldBeOfType<InProcessRateLimitCounters>();
    }

    /// <summary>
    /// The two budgets this file declares, read back from the counters they were handed to. These are
    /// the numbers <c>docs/CONFIGURATION.md</c> publishes and the integration suite spends.
    /// </summary>
    [Fact]
    public void BothPolicies_AskTheCountersForTheirPublishedBudget()
    {
        var counters = new RecordingCounters(RateLimitPartition.GetNoLimiter("permit-everything"));

        using var provider = BuildProvider(counters, RateLimiterWindow.Default);

        _ = OptionsOf(provider);

        counters.Budgets.ShouldBe(
            [
                new RateLimitBudget(RateLimitingExtensions.AuthenticationPermitLimit, TimeSpan.FromMinutes(1)),
                new RateLimitBudget(RateLimitingExtensions.GlobalPermitLimit, TimeSpan.FromMinutes(1)),
            ],
            ignoreOrder: true);
    }

    /// <summary>
    /// A partitioner is asked for once, while the host is composed — not per request. The limiter is
    /// the one component here that has to stay cheap while it is being attacked.
    /// </summary>
    [Fact]
    public void ThePartitioners_AreBuiltOnce_NotPerRequest()
    {
        var counters = new RecordingCounters(RateLimitPartition.GetNoLimiter("permit-everything"));

        using var provider = BuildProvider(counters, RateLimiterWindow.Default);
        using var globalLimiter = GlobalLimiterOf(provider);

        for (int request = 1; request <= 5; request++)
        {
            using var lease = globalLimiter.AttemptAcquire(CreateHttpContext("203.0.113.7"));
            lease.IsAcquired.ShouldBeTrue();
        }

        counters.Budgets.Count.ShouldBe(2, "one partitioner for each of the two policies, and no more");
    }

    /// <summary>
    /// The seam is load-bearing rather than decorative: the global limiter routes every request
    /// through the partitioner the registered counters built — and through the one built for the
    /// <em>global</em> budget, which is also what settles that the other budget went to the other
    /// policy.
    /// </summary>
    [Fact]
    public void TheGlobalLimiter_UsesThePartitionerBuiltForTheGlobalBudget()
    {
        var counters = new RecordingCounters(RateLimitPartition.GetNoLimiter("permit-everything"));

        using var provider = BuildProvider(counters, RateLimiterWindow.Default);
        var httpContext = CreateHttpContext("203.0.113.7");

        using var globalLimiter = GlobalLimiterOf(provider);
        using var lease = globalLimiter.AttemptAcquire(httpContext);

        lease.IsAcquired.ShouldBeTrue();

        var partitioned = counters.Partitioned.ShouldHaveSingleItem();

        partitioned.Context.ShouldBeSameAs(httpContext);
        partitioned.Budget.ShouldBe(
            new RateLimitBudget(RateLimitingExtensions.GlobalPermitLimit, RateLimiterWindow.Default.Duration));
    }

    /// <summary>
    /// The same statement read from the other end: nothing between the middleware and the counters
    /// second-guesses them, so counters that refuse are counters whose refusal stands.
    /// </summary>
    [Fact]
    public void AHostsCounters_DecideTheOutcome()
    {
        var oneThenNothing = RateLimitPartition.GetFixedWindowLimiter(
            "one-permit-per-hour",
            _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 1,
                Window = TimeSpan.FromHours(1),
                QueueLimit = 0,
            });

        using var provider = BuildProvider(new RecordingCounters(oneThenNothing), RateLimiterWindow.Default);
        var httpContext = CreateHttpContext("203.0.113.7");

        using var globalLimiter = GlobalLimiterOf(provider);

        using (var spent = globalLimiter.AttemptAcquire(httpContext))
        {
            spent.IsAcquired.ShouldBeTrue();
        }

        using var refused = globalLimiter.AttemptAcquire(httpContext);

        refused.IsAcquired.ShouldBeFalse(
            "the host's counters had one permit an hour, and the composition must not have quietly " +
            "kept a limiter of its own in front of them");
    }

    /// <summary>
    /// <c>RateLimiterWindow</c> is the one lever a host has over a limiter with no injectable clock,
    /// and the integration suite widens it to an hour to stay off a real window boundary. Reading it
    /// into the budget is what carries that lever across the <see cref="IRateLimitCounters"/> seam.
    /// </summary>
    [Fact]
    public void AReplacedWindow_ReachesTheCountersThroughBothBudgets()
    {
        var counters = new RecordingCounters(RateLimitPartition.GetNoLimiter("permit-everything"));

        using var provider = BuildProvider(counters, new RateLimiterWindow(TimeSpan.FromHours(3)));

        _ = OptionsOf(provider);

        counters.Budgets.Select(budget => budget.Window)
            .ShouldBe([TimeSpan.FromHours(3), TimeSpan.FromHours(3)]);
    }

    private static ServiceProvider BuildProvider(IRateLimitCounters counters, RateLimiterWindow window)
    {
        var services = new ServiceCollection().AddApiRateLimiting();

        services.Replace(ServiceDescriptor.Singleton(counters));
        services.Replace(ServiceDescriptor.Singleton(window));

        return services.BuildServiceProvider();
    }

    private static RateLimiterOptions OptionsOf(IServiceProvider provider) =>
        provider.GetRequiredService<IOptions<RateLimiterOptions>>().Value;

    private static PartitionedRateLimiter<HttpContext> GlobalLimiterOf(IServiceProvider provider)
    {
        var globalLimiter = OptionsOf(provider).GlobalLimiter;

        globalLimiter.ShouldNotBeNull("AddApiRateLimiting installs a global limiter.");

        return globalLimiter;
    }

    private static DefaultHttpContext CreateHttpContext(string address) =>
        new() { Connection = { RemoteIpAddress = IPAddress.Parse(address) } };

    private sealed record PartitionedRequest(RateLimitBudget Budget, HttpContext Context);

    private sealed class RecordingCounters(RateLimitPartition<string> partition) : IRateLimitCounters
    {
        /// <summary>The budgets a partitioner was asked for, in the order they were asked.</summary>
        public List<RateLimitBudget> Budgets { get; } = [];

        /// <summary>
        /// The requests those partitioners were handed, each tagged with the budget its partitioner
        /// was built for — which is what tells one policy's partitioner from the other's.
        /// </summary>
        public List<PartitionedRequest> Partitioned { get; } = [];

        public Func<HttpContext, RateLimitPartition<string>> PartitionerFor(RateLimitBudget budget)
        {
            Budgets.Add(budget);

            return httpContext =>
            {
                Partitioned.Add(new PartitionedRequest(budget, httpContext));

                return partition;
            };
        }
    }
}
