using AppTemplate.Application.Core.Common.Ports;
using AppTemplate.Application.Core.Features.Accounts.UseCases.Commands.SweepDeletedAccountData;
using AppTemplate.Domain.Core.Common.Primitives;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Shouldly;
using Xunit;

namespace AppTemplate.Application.Core.UnitTests.Features.Accounts.UseCases.Commands.SweepDeletedAccountData;

public sealed class SweepDeletedAccountDataUseCaseTests
{
    private static readonly UserId _firstOwner = UserId.Create(Guid.CreateVersion7());
    private static readonly UserId _secondOwner = UserId.Create(Guid.CreateVersion7());
    private static readonly UserId _thirdOwner = UserId.Create(Guid.CreateVersion7());

    private readonly IDeletedAccountDataPolicy _policy = Substitute.For<IDeletedAccountDataPolicy>();
    private readonly StubLeaderLease _lease = new();

    private static CancellationToken TestToken => TestContext.Current.CancellationToken;

    [Fact]
    public async Task EveryOwnerThatStillHasData_IsApplied()
    {
        GivenThePolicyLists(_firstOwner, _secondOwner);
        GivenThePolicyApplies();

        var result = await UseCase().ExecuteAsync(TestToken);

        result.IsSuccess.ShouldBeTrue();
        result.Value.ShouldBe(2);
        await _policy.Received(1).ApplyAsync(_firstOwner, TestToken);
        await _policy.Received(1).ApplyAsync(_secondOwner, TestToken);
    }

    /// <summary>
    /// The precondition is the policy's to re-derive: an owner whose account exists again — or never
    /// left — is listed like any other, and the sweep counts nothing for it.
    /// </summary>
    [Fact]
    public async Task AnOwnerWhosePolicyAppliedNothing_IsNotCounted()
    {
        GivenThePolicyLists(_firstOwner, _secondOwner);
        _policy.ApplyAsync(_firstOwner, Arg.Any<CancellationToken>()).Returns(true);
        _policy.ApplyAsync(_secondOwner, Arg.Any<CancellationToken>()).Returns(false);

        var result = await UseCase().ExecuteAsync(TestToken);

        result.Value.ShouldBe(1);
    }

    /// <summary>
    /// Resumption is per owner: one poisoned owner is skipped and logged, never allowed to stop
    /// its neighbours — and the owners swept before it are kept.
    /// </summary>
    [Fact]
    public async Task AnOwnerWhoseApplicationThrows_IsSkippedAndTheSweepContinues()
    {
        GivenThePolicyLists(_firstOwner, _secondOwner, _thirdOwner);
        _policy.ApplyAsync(Arg.Any<UserId>(), Arg.Any<CancellationToken>()).Returns(true);
        _policy.ApplyAsync(_secondOwner, Arg.Any<CancellationToken>())
            .ThrowsAsync(new InvalidOperationException("the poisoned owner"));

        var result = await UseCase().ExecuteAsync(TestToken);

        result.IsSuccess.ShouldBeTrue();
        result.Value.ShouldBe(2);
        await _policy.Received(1).ApplyAsync(_thirdOwner, TestToken);
    }

    /// <summary>
    /// One host at a time: a standby that did not take the lease runs nothing and reports zero,
    /// rather than reporting a failure that would look like a sweep that went wrong.
    /// </summary>
    [Fact]
    public async Task AHostThatDoesNotHoldTheLease_AppliesNothingAndReportsZero()
    {
        _lease.HoldsTheLease = false;
        GivenThePolicyLists(_firstOwner);

        var result = await UseCase().ExecuteAsync(TestToken);

        result.IsSuccess.ShouldBeTrue();
        result.Value.ShouldBe(0);
        await _policy.DidNotReceive().ListDataOwnerIdsAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task APolicyWithNoOwners_CallsApplyForNobody()
    {
        GivenThePolicyLists();

        var result = await UseCase().ExecuteAsync(TestToken);

        result.Value.ShouldBe(0);
        await _policy.DidNotReceive().ApplyAsync(Arg.Any<UserId>(), Arg.Any<CancellationToken>());
    }

    private void GivenThePolicyLists(params UserId[] owners) =>
        _policy.ListDataOwnerIdsAsync(Arg.Any<CancellationToken>())
            .Returns((IReadOnlyList<UserId>)[.. owners]);

    private void GivenThePolicyApplies() =>
        _policy.ApplyAsync(Arg.Any<UserId>(), Arg.Any<CancellationToken>()).Returns(true);

    private SweepDeletedAccountDataUseCase UseCase() =>
        new([_policy], _lease, NullLogger<SweepDeletedAccountDataUseCase>.Instance);

    /// <summary>
    /// Runs the work it is handed, or answers <c>false</c> without running it — the two outcomes
    /// <see cref="ILeaderLease"/> defines. The work is invoked, not just recorded, because what is
    /// under test is what the pass does while it holds the lease.
    /// </summary>
    private sealed class StubLeaderLease : ILeaderLease
    {
        public bool HoldsTheLease { get; set; } = true;

        public async Task<bool> TryRunExclusivelyAsync(
            string leaseName,
            Func<CancellationToken, Task> work,
            CancellationToken cancellationToken = default)
        {
            if (!HoldsTheLease)
            {
                return false;
            }

            await work(cancellationToken);

            return true;
        }
    }
}
