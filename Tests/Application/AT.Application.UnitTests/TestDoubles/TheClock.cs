using Microsoft.Extensions.Time.Testing;

namespace AppTemplate.Application.UnitTests.TestDoubles;

internal static class TheClock
{
    public static readonly DateTimeOffset DefaultInstant = new(2026, 8, 3, 12, 0, 0, TimeSpan.Zero);

    public static FakeTimeProvider Start() => At(DefaultInstant);

    public static FakeTimeProvider At(DateTimeOffset instant) => new(instant);
}
