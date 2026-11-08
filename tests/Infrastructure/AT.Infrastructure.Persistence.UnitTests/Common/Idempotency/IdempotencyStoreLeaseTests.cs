using AppTemplate.Infrastructure.Persistence.Common.Idempotency;
using Shouldly;
using Xunit;

namespace AppTemplate.Infrastructure.Persistence.UnitTests.Common.Idempotency;

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
