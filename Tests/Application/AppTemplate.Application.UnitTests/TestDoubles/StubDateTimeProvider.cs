using AppTemplate.Application.Common.Ports;

namespace AppTemplate.Application.UnitTests.TestDoubles;

internal sealed class StubDateTimeProvider(DateTimeOffset utcNow) : IDateTimeProvider
{
    public static readonly DateTimeOffset DefaultInstant = new(2026, 8, 3, 12, 0, 0, TimeSpan.Zero);

    public StubDateTimeProvider() : this(DefaultInstant)
    {
    }

    public DateTimeOffset UtcNow => utcNow;
}
