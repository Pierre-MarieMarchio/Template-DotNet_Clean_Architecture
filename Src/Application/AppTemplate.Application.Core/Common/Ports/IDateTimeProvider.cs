namespace AppTemplate.Application.Core.Common.Ports;

/// <summary>
/// The only clock this layer reads, so anything time-dependent is something a test sets rather than
/// waits for.
/// </summary>
public interface IDateTimeProvider
{
    /// <summary>Always UTC: nothing in this layer stores or compares a local time.</summary>
    DateTimeOffset UtcNow { get; }
}
