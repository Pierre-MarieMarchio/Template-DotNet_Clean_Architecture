using AppTemplate.Application.Common.Ports;

namespace AppTemplate.Infrastructure.Identity.UnitTests.Fixtures;

/// <summary>
/// A clock a test can move, for the one thing in this module that actually reads it.
/// <para>
/// Deliberately not <c>AppTemplate.Infrastructure.InMemory</c>'s <c>FixedDateTimeProvider</c>: this
/// project tests the identity module, and a reference to a second infrastructure module for one
/// three-line double would tie the two together for no reason.
/// </para>
/// <para>
/// Nothing else here is testable this way. JWT validation and ASP.NET Identity both read
/// <c>TimeProvider.System</c> and are untouched by this — see CONTRIBUTING.md.
/// </para>
/// </summary>
internal sealed class MovableDateTimeProvider : IDateTimeProvider
{
    public DateTimeOffset UtcNow { get; private set; } =
        new(2026, 1, 1, 12, 0, 0, TimeSpan.Zero);

    internal void Advance(TimeSpan by) => UtcNow = UtcNow.Add(by);
}
