using AppTemplate.Application.Common.Ports;

namespace AppTemplate.Infrastructure.Persistence.Common.Time;

/// <summary>
/// The real clock. It lives in infrastructure because reading the machine's time is an
/// interaction with the outside world, and it returns <see cref="DateTimeOffset.UtcNow"/> so
/// that no stored timestamp depends on the server's local zone: a column that mixed local and
/// UTC values would make every comparison, and every migration to a server in a different zone,
/// silently wrong.
/// <para>
/// One clock for the whole process, registered here rather than once per feature: two
/// features each supplying their own implementation is how a system ends up with two
/// different notions of "now", and made replacing it in a test a matter of which
/// registration happened last.
/// </para>
/// </summary>
internal sealed class SystemDateTimeProvider : IDateTimeProvider
{
    public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;
}
