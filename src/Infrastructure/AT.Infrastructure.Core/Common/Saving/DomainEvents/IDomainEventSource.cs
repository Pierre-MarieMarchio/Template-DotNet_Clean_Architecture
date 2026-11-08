using AppTemplate.Domain.Core.Common.Events;
using AppTemplate.Infrastructure.Core.Common.Saving.Tracking;

namespace AppTemplate.Infrastructure.Core.Common.Saving.DomainEvents;

public interface IDomainEventSource : IStagedOnContext
{
    IReadOnlyCollection<IDomainEvent> DrainDomainEvents();

    void Restore(IEnumerable<IDomainEvent> domainEvents);
}
