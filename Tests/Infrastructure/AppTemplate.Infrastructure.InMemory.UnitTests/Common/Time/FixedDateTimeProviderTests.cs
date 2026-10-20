using AppTemplate.Infrastructure.InMemory.Common.Time;
using Shouldly;
using Xunit;

namespace AppTemplate.Infrastructure.InMemory.UnitTests.Common.Time;

public sealed class FixedDateTimeProviderTests
{
    [Fact]
    public void UtcNow_StartsAtTheDefaultInstantRatherThanTheMachineClock() =>
        new FixedDateTimeProvider().UtcNow.ShouldBe(FixedDateTimeProvider.DefaultInstant);

    [Fact]
    public void DefaultInstant_IsExpressedInUtc() =>
        FixedDateTimeProvider.DefaultInstant.Offset.ShouldBe(TimeSpan.Zero);

    [Fact]
    public void UtcNow_DoesNotMoveWhileTheWallClockDoes()
    {
        var clock = new FixedDateTimeProvider();
        var first = clock.UtcNow;

        var wallClockAtStart = DateTimeOffset.UtcNow;
        SpinWait.SpinUntil(() => DateTimeOffset.UtcNow > wallClockAtStart, TimeSpan.FromSeconds(5))
            .ShouldBeTrue("the wall clock did not tick, so this test could not tell the two apart");

        clock.UtcNow.ShouldBe(first);
    }

    [Fact]
    public void Set_MovesTheClockToThatInstantInUtc()
    {
        var clock = new FixedDateTimeProvider();
        var noonInBerlin = new DateTimeOffset(2026, 3, 1, 12, 0, 0, TimeSpan.FromHours(2));

        clock.Set(noonInBerlin);

        clock.UtcNow.Offset.ShouldBe(TimeSpan.Zero);
        clock.UtcNow.ShouldBe(new DateTimeOffset(2026, 3, 1, 10, 0, 0, TimeSpan.Zero));
    }

    [Fact]
    public void Advance_MovesTheClockForwardByTheDelta()
    {
        var clock = new FixedDateTimeProvider();

        clock.Advance(TimeSpan.FromMinutes(15));

        clock.UtcNow.ShouldBe(FixedDateTimeProvider.DefaultInstant.AddMinutes(15));
    }

    [Fact]
    public void Advance_AccumulatesAcrossCalls()
    {
        var clock = new FixedDateTimeProvider();

        clock.Advance(TimeSpan.FromMinutes(10));
        clock.Advance(TimeSpan.FromMinutes(5));

        clock.UtcNow.ShouldBe(FixedDateTimeProvider.DefaultInstant.AddMinutes(15));
    }

    [Fact]
    public void Advance_AcceptsAZeroDelta()
    {
        var clock = new FixedDateTimeProvider();

        clock.Advance(TimeSpan.Zero);

        clock.UtcNow.ShouldBe(FixedDateTimeProvider.DefaultInstant);
    }

    [Fact]
    public void Advance_RejectsANegativeDeltaAndLeavesTheClockWhereItWas()
    {
        var clock = new FixedDateTimeProvider();
        clock.Advance(TimeSpan.FromMinutes(10));
        var before = clock.UtcNow;

        var exception = Should.Throw<ArgumentOutOfRangeException>(
            () => clock.Advance(TimeSpan.FromTicks(-1)));

        exception.ParamName.ShouldBe("delta");
        clock.UtcNow.ShouldBe(before);
    }

    [Fact]
    public void Reset_ReturnsTheClockToTheDefaultInstant()
    {
        var clock = new FixedDateTimeProvider();
        clock.Set(new DateTimeOffset(2030, 6, 1, 8, 30, 0, TimeSpan.Zero));
        clock.Advance(TimeSpan.FromDays(2));

        clock.Reset();

        clock.UtcNow.ShouldBe(FixedDateTimeProvider.DefaultInstant);
    }
}
