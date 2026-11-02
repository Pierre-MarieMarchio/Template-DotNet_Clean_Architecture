using AppTemplate.Application.Core.Common.Ports;

namespace AppTemplate.Infrastructure.InMemory.Common.Time;

public sealed class FixedDateTimeProvider : IDateTimeProvider
{
    public static readonly DateTimeOffset DefaultInstant =
        new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    private readonly object _gate = new();

    private DateTimeOffset _utcNow = DefaultInstant;

    public DateTimeOffset UtcNow
    {
        get
        {
            lock (_gate)
            {
                return _utcNow;
            }
        }
    }

    public void Set(DateTimeOffset instant)
    {
        lock (_gate)
        {
            _utcNow = instant.ToUniversalTime();
        }
    }

    public void Advance(TimeSpan delta)
    {
        if (delta < TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(
                nameof(delta),
                delta,
                "The clock only moves forward. Use Set to place it at an earlier instant.");
        }

        lock (_gate)
        {
            _utcNow = _utcNow.Add(delta);
        }
    }

    public void Reset()
    {
        lock (_gate)
        {
            _utcNow = DefaultInstant;
        }
    }
}
