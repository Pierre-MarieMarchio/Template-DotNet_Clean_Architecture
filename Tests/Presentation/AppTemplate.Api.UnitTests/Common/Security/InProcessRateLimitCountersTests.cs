using System.Net;
using System.Threading.RateLimiting;
using AppTemplate.Api.Common.Security;
using Microsoft.AspNetCore.Http;
using Shouldly;
using Xunit;

namespace AppTemplate.Api.UnitTests.Common.Security;

/// <summary>
/// The shipped counters, exercised through the same <c>PartitionedRateLimiter</c> the middleware
/// drives, because the guarantee under test is what that machinery does with what this type returns.
/// Asserting on the returned partition alone would prove only that a struct was built.
/// </summary>
/// <remarks>
/// The windows here are minutes or hours wide for the reason <c>ApiFactory</c> widens the product's:
/// the fixed-window limiter reads the wall clock and no clock can be injected into it, so a window a
/// test could plausibly outlive is a test that fails on a slow machine.
/// </remarks>
public sealed class InProcessRateLimitCountersTests
{
    private static readonly RateLimitBudget _threePerHour = new(PermitLimit: 3, Window: TimeSpan.FromHours(1));

    [Fact]
    public void PartitionerFor_KeysThePartition_ByClientAddress()
    {
        var counters = new InProcessRateLimitCounters();

        counters.PartitionerFor(_threePerHour)(CreateHttpContext("203.0.113.7"))
            .PartitionKey
            .ShouldBe("ip:203.0.113.7");
    }

    [Fact]
    public void ACallerSpendsOnePermitPerRequest_AndIsRefusedPastTheBudget()
    {
        using var limiter = CreateLimiter(_threePerHour);
        var caller = CreateHttpContext("203.0.113.7");

        for (int request = 1; request <= _threePerHour.PermitLimit; request++)
        {
            using var allowed = limiter.AttemptAcquire(caller);

            allowed.IsAcquired.ShouldBeTrue(
                $"request {request} of {_threePerHour.PermitLimit} is inside the budget");
        }

        using var refused = limiter.AttemptAcquire(caller);

        refused.IsAcquired.ShouldBeFalse();
    }

    /// <summary>
    /// The refusal carries the metadata <c>RateLimitingExtensions.OnRejected</c> turns into the
    /// <c>Retry-After</c> header. A counter that refused without it would still answer 429, and the
    /// header this API documents would disappear with nothing failing.
    /// </summary>
    [Fact]
    public void ARefusal_CarriesRetryAfter()
    {
        using var limiter = CreateLimiter(_threePerHour);
        var caller = CreateHttpContext("203.0.113.7");

        for (int request = 1; request <= _threePerHour.PermitLimit; request++)
        {
            using var allowed = limiter.AttemptAcquire(caller);
            allowed.IsAcquired.ShouldBeTrue();
        }

        using var refused = limiter.AttemptAcquire(caller);

        refused.TryGetMetadata(MetadataName.RetryAfter, out var retryAfter).ShouldBeTrue();
        retryAfter.ShouldBeGreaterThan(TimeSpan.Zero);
        retryAfter.ShouldBeLessThanOrEqualTo(_threePerHour.Window);
    }

    /// <summary>
    /// A caller over budget is told so rather than parked: with a queue, the awaiting request would
    /// hold a thread for the rest of the window, which is the opposite of shedding load. This is the
    /// assertion that the queue is really zero — <c>AttemptAcquire</c> would never queue anyway.
    /// </summary>
    [Fact]
    public async Task ACallerOverBudget_IsRefusedImmediatelyRatherThanQueued()
    {
        using var limiter = CreateLimiter(_threePerHour);
        var caller = CreateHttpContext("203.0.113.7");

        for (int request = 1; request <= _threePerHour.PermitLimit; request++)
        {
            using var allowed = await limiter.AcquireAsync(caller, cancellationToken: TestContext.Current.CancellationToken);
            allowed.IsAcquired.ShouldBeTrue();
        }

        using var refused = await limiter.AcquireAsync(caller, cancellationToken: TestContext.Current.CancellationToken);

        refused.IsAcquired.ShouldBeFalse();
    }

    /// <summary>
    /// The property the whole limiter rests on: one caller cannot spend another's budget.
    /// </summary>
    [Fact]
    public void ExhaustingOneAddressesBudget_LeavesAnotherAddressUnaffected()
    {
        using var limiter = CreateLimiter(_threePerHour);
        var exhausted = CreateHttpContext("203.0.113.7");
        var other = CreateHttpContext("203.0.113.8");

        for (int request = 1; request <= _threePerHour.PermitLimit; request++)
        {
            using var allowed = limiter.AttemptAcquire(exhausted);
            allowed.IsAcquired.ShouldBeTrue();
        }

        using var refused = limiter.AttemptAcquire(exhausted);
        refused.IsAcquired.ShouldBeFalse();

        using var fromTheOtherCaller = limiter.AttemptAcquire(other);
        fromTheOtherCaller.IsAcquired.ShouldBeTrue();
    }

    /// <summary>
    /// The budget is what the counters are told, and nothing about it is remembered from a previous
    /// call: a second budget over the same address counts separately.
    /// </summary>
    [Fact]
    public void TheBudget_DecidesHowManyPermitsAPartitionHolds()
    {
        var generous = new RateLimitBudget(PermitLimit: 10, Window: TimeSpan.FromHours(1));

        using var limiter = CreateLimiter(generous);
        var caller = CreateHttpContext("203.0.113.7");

        for (int request = 1; request <= generous.PermitLimit; request++)
        {
            using var allowed = limiter.AttemptAcquire(caller);
            allowed.IsAcquired.ShouldBeTrue($"request {request} is inside the wider budget");
        }

        using var refused = limiter.AttemptAcquire(caller);

        refused.IsAcquired.ShouldBeFalse();
    }

    private static PartitionedRateLimiter<HttpContext> CreateLimiter(RateLimitBudget budget)
    {
        var counters = new InProcessRateLimitCounters();

        return PartitionedRateLimiter.Create(counters.PartitionerFor(budget));
    }

    private static DefaultHttpContext CreateHttpContext(string address) =>
        new() { Connection = { RemoteIpAddress = IPAddress.Parse(address) } };
}
