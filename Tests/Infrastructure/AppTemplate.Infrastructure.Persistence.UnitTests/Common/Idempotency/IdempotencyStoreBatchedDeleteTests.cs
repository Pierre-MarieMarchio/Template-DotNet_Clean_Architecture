using AppTemplate.Infrastructure.Persistence.Common.Idempotency;
using Shouldly;
using Xunit;

namespace AppTemplate.Infrastructure.Persistence.UnitTests.Common.Idempotency;

/// <summary>
/// <see cref="IdempotencyStore.RunBatchedDeleteAsync"/> is the looping and summing rule behind the
/// batched purge, pulled out of the EF-backed method so it can be exercised without a database.
/// Whether EF actually translates <c>Where().OrderBy().Take().ExecuteDeleteAsync()</c> into the
/// intended SQL is not covered here — that needs a real PostgreSQL and belongs to the integration
/// suite.
/// </summary>
public sealed class IdempotencyStoreBatchedDeleteTests
{
    [Fact]
    public async Task RunBatchedDeleteAsync_StopsAfterOneCall_WhenTheFirstBatchIsSmallerThanTheBatchSize()
    {
        var calls = 0;

        int total = await IdempotencyStore.RunBatchedDeleteAsync(
            batchSize: 100,
            deleteBatchAsync: _ =>
            {
                calls++;
                return Task.FromResult(37);
            },
            ct: TestContext.Current.CancellationToken);

        total.ShouldBe(37);
        calls.ShouldBe(1, "a batch smaller than the requested size means there is nothing left to delete");
    }

    [Fact]
    public async Task RunBatchedDeleteAsync_KeepsGoing_WhileEveryBatchIsFull()
    {
        var remainingFullBatches = 3;

        int total = await IdempotencyStore.RunBatchedDeleteAsync(
            batchSize: 100,
            deleteBatchAsync: _ =>
            {
                if (remainingFullBatches > 0)
                {
                    remainingFullBatches--;
                    return Task.FromResult(100);
                }

                return Task.FromResult(42);
            },
            ct: TestContext.Current.CancellationToken);

        // Three full batches of 100 plus one partial batch of 42.
        total.ShouldBe(342);
    }

    [Fact]
    public async Task RunBatchedDeleteAsync_ReturnsZero_WhenNothingIsExpired()
    {
        int total = await IdempotencyStore.RunBatchedDeleteAsync(
            batchSize: 1000,
            deleteBatchAsync: _ => Task.FromResult(0),
            ct: TestContext.Current.CancellationToken);

        total.ShouldBe(0);
    }

    [Fact]
    public async Task RunBatchedDeleteAsync_ForwardsTheCancellationTokenToEveryBatch()
    {
        using var cancellation = new CancellationTokenSource();
        CancellationToken? observed = null;

        await IdempotencyStore.RunBatchedDeleteAsync(
            batchSize: 10,
            deleteBatchAsync: ct =>
            {
                observed = ct;
                return Task.FromResult(3);
            },
            ct: cancellation.Token);

        observed.ShouldBe(cancellation.Token);
    }

    /// <summary>
    /// A batch exactly equal to the batch size looks the same to this rule as "there might be
    /// more" — it cannot distinguish "exactly one page left" from "at least one more page", and it
    /// must not guess. One extra, empty-returning call is the price of never leaving rows behind.
    /// </summary>
    [Fact]
    public async Task RunBatchedDeleteAsync_MakesOneMoreCall_WhenTheLastBatchExactlyFillsTheBatchSize()
    {
        var calls = 0;

        int total = await IdempotencyStore.RunBatchedDeleteAsync(
            batchSize: 50,
            deleteBatchAsync: _ =>
            {
                calls++;
                return Task.FromResult(calls == 1 ? 50 : 0);
            },
            ct: TestContext.Current.CancellationToken);

        calls.ShouldBe(2);
        total.ShouldBe(50);
    }
}
