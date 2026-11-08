namespace AppTemplate.Domain.Core.Common.Events;

public interface IDomainEvent
{
    DateTimeOffset OccurredOn { get; }
}
