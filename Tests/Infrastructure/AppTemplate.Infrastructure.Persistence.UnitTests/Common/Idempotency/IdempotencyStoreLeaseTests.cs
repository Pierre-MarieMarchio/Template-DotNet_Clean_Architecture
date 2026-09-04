using AppTemplate.Infrastructure.Persistence.Common.Idempotency;
using Shouldly;
using Xunit;

namespace AppTemplate.Infrastructure.Persistence.UnitTests.Common.Idempotency;

/// <summary>
/// <see cref="IdempotencyStore.HasExpiredLease"/> is the pure rule behind reclaiming a claim its
/// original owner never finished: it decides whether a row is fair game for a retry to take over,
/// with none of the atomicity that makes the actual reclaim in <c>ClaimAsync</c> safe under
/// concurrency — that half needs a real database and is covered by the integration suite instead.
/// </summary>
public sealed class IdempotencyStoreLeaseTests
{
    private static readonly DateTimeOffset _now = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    [Fact]
    public void HasExpiredLease_IsFalse_WhileTheLeaseIsStillInTheFuture()
    {
        var record = ARecord(isCompleted: false, claimedUntil: _now.AddMinutes(1));

        IdempotencyStore.HasExpiredLease(record, _now).ShouldBeFalse();
    }

    [Fact]
    public void HasExpiredLease_IsTrue_OnceTheLeaseHasPassed_AndTheClaimIsStillUnfinished()
    {
        var record = ARecord(isCompleted: false, claimedUntil: _now.AddMinutes(-1));

        IdempotencyStore.HasExpiredLease(record, _now).ShouldBeTrue();
    }

    [Fact]
    public void HasExpiredLease_IsTrue_AtTheExactInstantTheLeaseExpires()
    {
        var record = ARecord(isCompleted: false, claimedUntil: _now);

        IdempotencyStore.HasExpiredLease(record, _now).ShouldBeTrue();
    }

    /// <summary>
    /// A completed claim is never reclaimable regardless of what its lease says: completion is what
    /// retires the lease, and <see cref="IdempotencyStore.Decide"/> — not a reclaim — is what answers
    /// for it from there on.
    /// </summary>
    [Fact]
    public void HasExpiredLease_IsFalse_ForACompletedClaim_EvenWithALeaseInThePast()
    {
        var record = ARecord(isCompleted: true, claimedUntil: _now.AddMinutes(-1));

        IdempotencyStore.HasExpiredLease(record, _now).ShouldBeFalse();
    }

    private static IdempotencyRecord ARecord(bool isCompleted, DateTimeOffset claimedUntil) => new()
    {
        UserId = Guid.Parse("11111111-1111-1111-1111-111111111111"),
        Key = "a-key",
        Endpoint = "POST /api/v1/todo-lists",
        Fingerprint = new string('a', 64),
        IsCompleted = isCompleted,
        ClaimedUntil = claimedUntil,
        CreatedAt = _now,
        ExpiresAt = _now.AddDays(1),
    };
}
