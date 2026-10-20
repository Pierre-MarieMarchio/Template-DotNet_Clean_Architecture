namespace AppTemplate.Application.Core.Common.Ports;

public interface IDateTimeProvider
{
    DateTimeOffset UtcNow { get; }
}
