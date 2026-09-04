namespace AppTemplate.Application.Common.Ports;

public interface IDateTimeProvider
{
    DateTimeOffset UtcNow { get; }
}
