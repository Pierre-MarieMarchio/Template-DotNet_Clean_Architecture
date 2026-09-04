namespace AppTemplate.Domain.Common.Events;

/// <summary>
/// Something that happened in the domain, expressed in the past tense.
/// Raised by an aggregate root and dispatched after the transaction commits.
/// </summary>
public interface IDomainEvent
{
    /// <summary>When the event occurred, in UTC.</summary>
    DateTimeOffset OccurredOn { get; }
}
