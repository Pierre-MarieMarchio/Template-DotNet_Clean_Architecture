using AppTemplate.Application.Common.Abstractions;

namespace AppTemplate.Infrastructure.Persistence.Common.Time;

/// <summary>
/// The real clock. It lives in infrastructure because reading the machine's time is an
/// interaction with the outside world, and it returns <see cref="DateTimeOffset.UtcNow"/> so
/// that no stored timestamp depends on the server's local zone — the previous code mixed
/// <c>DateTime.Now</c> and <c>DateTime.UtcNow</c> in the same database.
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
