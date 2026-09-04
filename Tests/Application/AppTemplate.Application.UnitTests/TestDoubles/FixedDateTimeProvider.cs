using AppTemplate.Application.Common.Abstractions;

namespace AppTemplate.Application.UnitTests.TestDoubles;

internal sealed class FixedDateTimeProvider(DateTimeOffset utcNow) : IDateTimeProvider
{
    public static readonly DateTimeOffset DefaultInstant = new(2026, 8, 3, 12, 0, 0, TimeSpan.Zero);

    public FixedDateTimeProvider() : this(DefaultInstant)
    {
    }

    public DateTimeOffset UtcNow => utcNow;
}
