using AppTemplate.Application.Core.Common.Ports;

namespace AppTemplate.Infrastructure.Core.Common.Time;

internal sealed class SystemDateTimeProvider : IDateTimeProvider
{
    public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;
}
