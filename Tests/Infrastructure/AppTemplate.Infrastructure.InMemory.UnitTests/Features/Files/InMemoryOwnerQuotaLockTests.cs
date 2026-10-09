using AppTemplate.Application.Features.Files.Ports.OwnerQuotaLock;
using AppTemplate.Domain.Core.Common.Primitives;
using AppTemplate.Domain.Features.Files.ValueObjects;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using Shouldly;
using Xunit;

namespace AppTemplate.Infrastructure.InMemory.UnitTests.Features.Files;

/// <summary>
/// The in-process quota lock, reached through its port as <c>AddInMemoryOwnerQuotaLock</c>
/// registers it. Each contender is started from inside the holder's work, so the overlap is a fact.
/// </summary>
public sealed class InMemoryOwnerQuotaLockTests
{
    private static readonly UserId _owner = UserId.Create(Guid.CreateVersion7());
    private static readonly FileCategory _document = FileCategory.Create("document");
    private static readonly FileCategory _image = FileCategory.Create("image");

    private static CancellationToken TestToken => TestContext.Current.CancellationToken;

    [Fact]
    public async Task ASecondCallerForTheSameKey_WaitsUntilTheFirstHasFinished()
    {
        var quotaLock = Compose();
        Task<int>? contender = null;

        await quotaLock.RunExclusivelyAsync(_owner, _document, async _ =>
        {
            contender = quotaLock.RunExclusivelyAsync(_owner, _document, _ => Task.FromResult(2), TestToken);
            await Task.Delay(TimeSpan.FromMilliseconds(50), TestToken);

            contender.IsCompleted.ShouldBeFalse("the holder is still inside its work.");

            return 1;
        }, TestToken);

        (await contender!.WaitAsync(TimeSpan.FromSeconds(30), TestToken)).ShouldBe(2);
    }

    [Fact]
    public async Task AnotherCategoryOrAnotherOwner_IsNotBlocked()
    {
        var quotaLock = Compose();

        int ran = await quotaLock.RunExclusivelyAsync(_owner, _document, async _ =>
        {
            int other = await quotaLock
                .RunExclusivelyAsync(_owner, _image, _ => Task.FromResult(1), TestToken)
                .WaitAsync(TimeSpan.FromSeconds(30), TestToken);

            other += await quotaLock
                .RunExclusivelyAsync(UserId.Create(Guid.CreateVersion7()), _document, _ => Task.FromResult(1), TestToken)
                .WaitAsync(TimeSpan.FromSeconds(30), TestToken);

            return other;
        }, TestToken);

        ran.ShouldBe(2);
    }

    [Fact]
    public async Task WorkThatThrows_PropagatesAndReleasesTheLock()
    {
        var quotaLock = Compose();

        await Should.ThrowAsync<InvalidOperationException>(() => quotaLock.RunExclusivelyAsync<int>(
            _owner,
            _document,
            _ => throw new InvalidOperationException("the commit failed"),
            TestToken));

        (await quotaLock.RunExclusivelyAsync(_owner, _document, _ => Task.FromResult(1), TestToken)
            .WaitAsync(TimeSpan.FromSeconds(30), TestToken)).ShouldBe(1);
    }

    [Fact]
    public async Task AWaitPastTheLimit_ThrowsTheTimeoutAndDoesNotRunTheWork()
    {
        var quotaLock = Compose(TimeSpan.FromMilliseconds(50));
        bool contenderRan = false;

        await quotaLock.RunExclusivelyAsync(_owner, _document, async _ =>
        {
            await Should.ThrowAsync<OwnerQuotaLockTimeoutException>(() => quotaLock.RunExclusivelyAsync(
                _owner,
                _document,
                _ =>
                {
                    contenderRan = true;

                    return Task.FromResult(0);
                },
                TestToken));

            return 0;
        }, TestToken);

        contenderRan.ShouldBeFalse();
        (await quotaLock.RunExclusivelyAsync(_owner, _document, _ => Task.FromResult(1), TestToken)).ShouldBe(1);
    }

    [Fact]
    public async Task ACancelledWait_LeavesTheLockUsable()
    {
        var quotaLock = Compose();
        using var cancellation = new CancellationTokenSource();

        await quotaLock.RunExclusivelyAsync(_owner, _document, async _ =>
        {
            var waiting = quotaLock.RunExclusivelyAsync(_owner, _document, _ => Task.FromResult(0), cancellation.Token);
            await cancellation.CancelAsync();

            await Should.ThrowAsync<OperationCanceledException>(() => waiting);

            return 0;
        }, TestToken);

        (await quotaLock.RunExclusivelyAsync(_owner, _document, _ => Task.FromResult(1), TestToken)
            .WaitAsync(TimeSpan.FromSeconds(30), TestToken)).ShouldBe(1);
    }

    [Fact]
    public void AddInMemoryOwnerQuotaLock_ReplacesALockAlreadyRegistered()
    {
        var services = new ServiceCollection();
        var real = Substitute.For<IOwnerQuotaLock>();
        services.AddSingleton(real);

        services.AddInMemoryOwnerQuotaLock();

        using var provider = services.BuildServiceProvider(validateScopes: true);
        provider.GetServices<IOwnerQuotaLock>().ShouldHaveSingleItem().ShouldNotBeSameAs(real);
    }

    [Fact]
    public void AddInMemoryModule_KeepsTheDatabaseLock()
    {
        var services = new ServiceCollection();
        var real = Substitute.For<IOwnerQuotaLock>();
        services.AddSingleton(real);

        services.AddInMemoryModule();

        using var provider = services.BuildServiceProvider(validateScopes: true);
        provider.GetRequiredService<IOwnerQuotaLock>().ShouldBeSameAs(real);
    }

    private static IOwnerQuotaLock Compose(TimeSpan? waitLimit = null) =>
        new ServiceCollection()
            .AddInMemoryOwnerQuotaLock(waitLimit)
            .BuildServiceProvider(validateScopes: true)
            .GetRequiredService<IOwnerQuotaLock>();
}
