using Microsoft.Extensions.Time.Testing;

namespace AppTemplate.Infrastructure.Auth.UnitTests.Fixtures;

internal static class TheClock
{
    public static readonly DateTimeOffset DefaultInstant = new(2026, 1, 1, 12, 0, 0, TimeSpan.Zero);

    public static FakeTimeProvider Start() => At(DefaultInstant);

    public static FakeTimeProvider At(DateTimeOffset instant) => new(instant);
}
