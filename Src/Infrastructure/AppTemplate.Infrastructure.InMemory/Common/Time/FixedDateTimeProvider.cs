using AppTemplate.Application.Common.Abstractions;

namespace AppTemplate.Infrastructure.InMemory.Common.Time;

/// <summary>
/// A clock that does not move unless it is told to. Time-dependent behaviour — token expiry,
/// refresh-token rotation, audit stamps — is then a matter of arithmetic rather than of
/// waiting, and a test that asserts on an expiry no longer has to sleep or to tolerate a
/// window.
/// <para>
/// Public, and public for a reason: the whole point is that the test controls it, so
/// <see cref="Set"/> and <see cref="Advance"/> have to cross the assembly boundary. Resolve
/// the concrete type to move time; production code resolves <see cref="IDateTimeProvider"/>
/// and cannot tell the difference.
/// </para>
/// <para>
/// Reads and writes are locked. An integration test drives the API from several requests, and
/// a clock that could be read while being advanced would produce a torn value on a 32-bit
/// runtime and an unreproducible failure on any of them.
/// </para>
/// </summary>
public sealed class FixedDateTimeProvider : IDateTimeProvider
{
    /// <summary>
    /// An arbitrary but fixed instant, in UTC, chosen so that a test that forgets to set the
    /// clock still gets a stable value instead of the machine's time.
    /// </summary>
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

    /// <summary>Moves the clock to an exact instant, converted to UTC.</summary>
    public void Set(DateTimeOffset instant)
    {
        lock (_gate)
        {
            _utcNow = instant.ToUniversalTime();
        }
    }

    /// <summary>
    /// Moves the clock forward. A negative <paramref name="delta"/> is rejected rather than
    /// quietly rewinding: a clock that goes backwards makes an expiry check pass twice, which
    /// is a test that proves nothing.
    /// </summary>
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

    /// <summary>Returns the clock to <see cref="DefaultInstant"/>.</summary>
    public void Reset()
    {
        lock (_gate)
        {
            _utcNow = DefaultInstant;
        }
    }
}
