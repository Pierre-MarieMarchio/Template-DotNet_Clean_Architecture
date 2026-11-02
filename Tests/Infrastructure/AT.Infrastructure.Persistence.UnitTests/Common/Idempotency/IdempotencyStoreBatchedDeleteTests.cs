using AppTemplate.Infrastructure.Persistence.Common.Idempotency;
using Shouldly;
using Xunit;

namespace AppTemplate.Infrastructure.Persistence.UnitTests.Common.Idempotency;

public sealed class IdempotencyStoreBatchedDeleteTests
{
    [Fact]
    public async Task RunBatchedDeleteAsync_StopsAfterOneCall_WhenTheFirstBatchIsSmallerThanTheBatchSize()
    {
        int calls = 0;

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
        const int batchSize = 100;
        const int lastPartialBatch = 42;
        int remainingFullBatches = 3;

        int total = await IdempotencyStore.RunBatchedDeleteAsync(
            batchSize: batchSize,
            deleteBatchAsync: _ =>
            {
                if (remainingFullBatches > 0)
                {
                    remainingFullBatches--;
                    return Task.FromResult(batchSize);
                }

                return Task.FromResult(lastPartialBatch);
            },
            ct: TestContext.Current.CancellationToken);

        total.ShouldBe(3 * batchSize + lastPartialBatch);
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
    public async Task RunBatchedDeleteAsync_MakesOneMoreCall_WhenTheLastBatchExactlyFillsTheBatchSize()
    {
        int calls = 0;

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
