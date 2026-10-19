using AppTemplate.Api.IntegrationTests.Infrastructure;
using AppTemplate.Application.Features.Files.Ports.OwnerQuotaLock;
using AppTemplate.Domain.Core.Common.Primitives;
using AppTemplate.Domain.Features.Files.Model.ValueObjects;
using Shouldly;
using Xunit;

namespace AppTemplate.Api.IntegrationTests.Files;

/// <summary>
/// The owner's quota lock against a real PostgreSQL, taken by two independently composed hosts.
/// Each contender is started from inside the holder's work, so the overlap is a fact.
/// </summary>
public sealed class OwnerQuotaLockTests(LeaseFixture fixture) : IClassFixture<LeaseFixture>
{
    private static readonly FileCategory _document = FileCategory.Create("document");
    private static readonly FileCategory _image = FileCategory.Create("image");

    private static readonly TimeSpan _cap = TimeSpan.FromSeconds(60);

    private static CancellationToken TestToken => TestContext.Current.CancellationToken;

    [Fact]
    public async Task ASecondHost_WaitsForTheFirstToFinish()
    {
        var owner = UserId.Create(Guid.CreateVersion7());
        var holder = fixture.CreateQuotaLock();
        var contender = fixture.CreateQuotaLock();
        Task<int>? waiting = null;

        await holder.RunExclusivelyAsync(owner, _document, async _ =>
        {
            waiting = contender.RunExclusivelyAsync(owner, _document, _ => Task.FromResult(2), TestToken);
            await Task.Delay(TimeSpan.FromSeconds(1), TestToken);

            waiting.IsCompleted.ShouldBeFalse("the first host still holds the lock for this owner and category.");

            return 1;
        }, TestToken);

        (await waiting!.WaitAsync(_cap, TestToken)).ShouldBe(2);
    }

    [Fact]
    public async Task AnotherCategoryOfTheSameOwner_IsNotBlocked()
    {
        var owner = UserId.Create(Guid.CreateVersion7());
        var holder = fixture.CreateQuotaLock();
        var other = fixture.CreateQuotaLock();

        int result = await holder.RunExclusivelyAsync(owner, _document, _ =>
            other.RunExclusivelyAsync(owner, _image, _ => Task.FromResult(1), TestToken).WaitAsync(_cap, TestToken),
            TestToken);

        result.ShouldBe(1);
    }

    [Fact]
    public async Task WorkThatThrows_ReleasesTheLockForAnotherHost()
    {
        var owner = UserId.Create(Guid.CreateVersion7());

        await Should.ThrowAsync<InvalidOperationException>(() => fixture.CreateQuotaLock().RunExclusivelyAsync<int>(
            owner,
            _document,
            _ => throw new InvalidOperationException("the commit failed"),
            TestToken));

        (await fixture.CreateQuotaLock()
            .RunExclusivelyAsync(owner, _document, _ => Task.FromResult(1), TestToken)
            .WaitAsync(_cap, TestToken)).ShouldBe(1);
    }

    /// <summary>
    /// <c>lock_timeout</c> bounds the wait on an advisory lock; the contender is refused with the
    /// port's exception and never runs its work. Takes the adapter's whole wait limit.
    /// </summary>
    [Fact]
    public async Task AWaitPastTheLimit_IsRefusedWithoutRunningTheWork()
    {
        var owner = UserId.Create(Guid.CreateVersion7());
        var holder = fixture.CreateQuotaLock();
        var contender = fixture.CreateQuotaLock();
        bool contenderRan = false;

        await holder.RunExclusivelyAsync(owner, _document, async _ =>
        {
            await Should.ThrowAsync<OwnerQuotaLockTimeoutException>(() => contender
                .RunExclusivelyAsync(
                    owner,
                    _document,
                    _ =>
                    {
                        contenderRan = true;

                        return Task.FromResult(0);
                    },
                    TestToken)
                .WaitAsync(_cap, TestToken));

            return 0;
        }, TestToken);

        contenderRan.ShouldBeFalse();
    }
}
