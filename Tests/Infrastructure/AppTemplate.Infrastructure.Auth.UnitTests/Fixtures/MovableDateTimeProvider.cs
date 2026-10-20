using AppTemplate.Application.Core.Common.Ports;

namespace AppTemplate.Infrastructure.Auth.UnitTests.Fixtures;

internal sealed class MovableDateTimeProvider : IDateTimeProvider
{
    internal MovableDateTimeProvider()
        : this(new DateTimeOffset(2026, 1, 1, 12, 0, 0, TimeSpan.Zero))
    {
    }

    internal MovableDateTimeProvider(DateTimeOffset start) => UtcNow = start;

    public DateTimeOffset UtcNow { get; private set; }

    internal void Advance(TimeSpan by) => UtcNow = UtcNow.Add(by);
}
