using System.Globalization;
using AppTemplate.Api.IntegrationTests.Infrastructure;
using AppTemplate.Application.Core.Common.Ports;
using Shouldly;
using Xunit;

namespace AppTemplate.Api.IntegrationTests.Leases;

public sealed class LeaderLeaseTests(LeaseFixture fixture) : IClassFixture<LeaseFixture>
{
    private static readonly TimeSpan _refusalCap = TimeSpan.FromSeconds(30);

    private static CancellationToken TestToken => TestContext.Current.CancellationToken;

    [Fact]
    public async Task ASecondHost_AskingForALeaseThatIsHeld_IsRefusedAndRunsNothing()
    {
        string leaseName = LeaseName("held");

        var holder = fixture.CreateLease();
        var standby = fixture.CreateLease();

        bool standbyRanItsWork = false;
        bool standbyTookTheLease = true;

        bool holderTookTheLease = await WhileHoldingAsync(holder, leaseName, async () =>
            standbyTookTheLease = await WithoutWaitingAsync(
                standby.TryRunExclusivelyAsync(
                    leaseName,
                    _ =>
                    {
                        standbyRanItsWork = true;

                        return Task.CompletedTask;
                    },
                    TestToken)));

        holderTookTheLease.ShouldBeTrue("nothing else held this lease, so the first host must get it.");

        standbyTookTheLease.ShouldBeFalse(
            "two hosts naming the same lease have to contend. This one was told it had taken a lease " +
            "the other was still inside, which is every replica believing it is the leader.");

        standbyRanItsWork.ShouldBeFalse(
            "a refused host must not run the work at all. Running it and answering false would be the " +
            "duplicated pass the lease exists to prevent, with nothing in the answer to show for it.");
    }

    [Fact]
    public async Task AHostThatFinishedItsWork_LeavesTheLeaseToTheNextOne()
    {
        string leaseName = LeaseName("released-after-success");

        bool firstTookTheLease = await fixture.CreateLease()
            .TryRunExclusivelyAsync(leaseName, _ => Task.CompletedTask, TestToken);

        firstTookTheLease.ShouldBeTrue();

        int secondRanItsWork = 0;

        bool secondTookTheLease = await fixture.CreateLease().TryRunExclusivelyAsync(
            leaseName,
            _ =>
            {
                secondRanItsWork++;

                return Task.CompletedTask;
            },
            TestToken);

        secondTookTheLease.ShouldBeTrue(
            "the lease is released when the work ends. A lease that is never given up is not " +
            "exclusion, it is one pass followed by silence for as long as the process lives.");

        secondRanItsWork.ShouldBe(1);
    }

    [Fact]
    public async Task WorkThatThrows_ReachesTheCallerAndStillLeavesTheLeaseBehind()
    {
        string leaseName = LeaseName("released-after-throw");

        var thrown = await Should.ThrowAsync<InvalidOperationException>(() =>
            fixture.CreateLease().TryRunExclusivelyAsync(
                leaseName,
                _ => throw new InvalidOperationException("the guarded work failed"),
                TestToken));

        thrown.Message.ShouldBe(
            "the guarded work failed",
            "a failed pass must not be reported as false: false means somebody else has it, and a " +
            "caller that could not tell the two apart would log a crash as a quiet standby.");

        bool nextTookTheLease = await fixture.CreateLease()
            .TryRunExclusivelyAsync(leaseName, _ => Task.CompletedTask, TestToken);

        nextTookTheLease.ShouldBeTrue(
            "the release is in a finally, so the throwing path gives the lease up like any other. " +
            "Without it one failed pass would leave the lease held until the process died.");
    }

    [Fact]
    public async Task AHostHoldingOneLease_LeavesADifferentlyNamedOneFree()
    {
        string held = LeaseName("one-name");
        string other = LeaseName("another-name");

        var holder = fixture.CreateLease();
        var neighbour = fixture.CreateLease();

        bool neighbourRanItsWork = false;
        bool neighbourTookTheLease = false;

        bool holderTookTheLease = await WhileHoldingAsync(holder, held, async () =>
            neighbourTookTheLease = await WithoutWaitingAsync(
                neighbour.TryRunExclusivelyAsync(
                    other,
                    _ =>
                    {
                        neighbourRanItsWork = true;

                        return Task.CompletedTask;
                    },
                    TestToken)));

        holderTookTheLease.ShouldBeTrue();

        neighbourTookTheLease.ShouldBeTrue(
            "two lease names name two different things and must not contend. This one was refused " +
            "while a lease of another name was held, so the key is not a function of the name.");

        neighbourRanItsWork.ShouldBeTrue();
    }

    private static Task<bool> WhileHoldingAsync(ILeaderLease holder, string leaseName, Func<Task> whileHeld) =>
        holder.TryRunExclusivelyAsync(leaseName, async _ => await whileHeld(), TestToken);

    private static async Task<bool> WithoutWaitingAsync(Task<bool> attempt)
    {
        try
        {
            return await attempt.WaitAsync(_refusalCap, TestToken);
        }
        catch (TimeoutException)
        {
            throw new ShouldAssertException(
                $"An attempt on a held lease was still waiting after {_refusalCap.TotalSeconds} " +
                "seconds. Taking a lease never queues, and a standby that queued would run the " +
                "work late instead of not at all — for a scheduled pass, the same thing done twice.");
        }
    }

    private static string LeaseName(string purpose)
    {
        string suffix = Guid.CreateVersion7().ToString("N", CultureInfo.InvariantCulture)[..12];

        return $"apptemplate-tests:{purpose}:{suffix}";
    }
}
