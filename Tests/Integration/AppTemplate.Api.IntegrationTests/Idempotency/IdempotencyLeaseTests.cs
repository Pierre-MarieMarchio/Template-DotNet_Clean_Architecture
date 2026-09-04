using AppTemplate.Api.IntegrationTests.Infrastructure;
using AppTemplate.Application.Common.Idempotency;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Xunit;

namespace AppTemplate.Api.IntegrationTests.Idempotency;

/// <summary>
/// The lease on an unfinished claim, exercised against real PostgreSQL through
/// <see cref="IIdempotencyStore"/> directly rather than over HTTP: reproducing "the process behind a
/// claim died" needs a claim nobody ever completes or releases, which no real request the filter
/// drives can ever leave behind on its own.
/// </summary>
public sealed class IdempotencyLeaseTests(ApiFixture fixture) : IntegrationTestBase(fixture)
{
    [Fact]
    public async Task AClaimWhoseLeaseHasNotExpired_BlocksARetry_AsInProgress()
    {
        await using var scope = Fixture.Factory.Services.CreateAsyncScope();
        var store = scope.ServiceProvider.GetRequiredService<IIdempotencyStore>();
        var key = AKey("still-leased");
        var now = Clock.UtcNow;

        (await store.ClaimAsync(key, now.AddHours(24), now.AddMinutes(15), TestToken)).Outcome
            .ShouldBe(IdempotencyOutcome.Claimed);

        // Nobody ever calls CompleteAsync or ReleaseAsync: the process behind the claim died.
        var retry = await store.ClaimAsync(key, now.AddHours(24), now.AddMinutes(15), TestToken);

        retry.Outcome.ShouldBe(IdempotencyOutcome.InProgress, "the lease has not run out yet");
    }

    [Fact]
    public async Task OnceTheLeaseExpires_ARetryReclaimsTheAbandonedClaim()
    {
        await using var scope = Fixture.Factory.Services.CreateAsyncScope();
        var store = scope.ServiceProvider.GetRequiredService<IIdempotencyStore>();
        var key = AKey("abandoned");
        var claimedAt = Clock.UtcNow;

        (await store.ClaimAsync(key, claimedAt.AddHours(24), claimedAt.AddMinutes(15), TestToken)).Outcome
            .ShouldBe(IdempotencyOutcome.Claimed);

        Clock.Advance(TimeSpan.FromMinutes(16));
        var now = Clock.UtcNow;

        var reclaimed = await store.ClaimAsync(key, now.AddHours(24), now.AddMinutes(15), TestToken);

        reclaimed.Outcome.ShouldBe(
            IdempotencyOutcome.Claimed,
            "past its lease, an unfinished claim must not block a retry for the rest of its 24-hour retention");
    }

    /// <summary>
    /// The atomicity the whole mechanism depends on: two retries racing to reclaim the very same
    /// abandoned key must not both succeed, the exact double claim that would let both go on to write
    /// for real.
    /// </summary>
    [Fact]
    public async Task TwoConcurrentRetries_RacingToReclaimTheSameAbandonedKey_OnlyOneWins()
    {
        await using var scope = Fixture.Factory.Services.CreateAsyncScope();
        await using var otherScope = Fixture.Factory.Services.CreateAsyncScope();
        var store = scope.ServiceProvider.GetRequiredService<IIdempotencyStore>();
        var otherStore = otherScope.ServiceProvider.GetRequiredService<IIdempotencyStore>();
        var key = AKey("raced");
        var claimedAt = Clock.UtcNow;

        (await store.ClaimAsync(key, claimedAt.AddHours(24), claimedAt.AddMinutes(15), TestToken)).Outcome
            .ShouldBe(IdempotencyOutcome.Claimed);

        Clock.Advance(TimeSpan.FromMinutes(16));
        var now = Clock.UtcNow;

        var results = await Task.WhenAll(
            store.ClaimAsync(key, now.AddHours(24), now.AddMinutes(15), TestToken),
            otherStore.ClaimAsync(key, now.AddHours(24), now.AddMinutes(15), TestToken));

        results.Count(claim => claim.Outcome == IdempotencyOutcome.Claimed).ShouldBe(
            1,
            "exactly one of two concurrent retries may reclaim an abandoned key");
        results.Count(claim => claim.Outcome == IdempotencyOutcome.InProgress).ShouldBe(1);
    }

    private static IdempotencyKey AKey(string key) =>
        IdempotencyKey.Create(
            userId: Guid.NewGuid(),
            key: key,
            endpoint: "POST /api/v1/todo-lists",
            fingerprint: new string('a', 64),
            maxKeyLength: 512).Value;
}
