using AppTemplate.Domain.Core.Common.Events;
using Microsoft.EntityFrameworkCore;

namespace AppTemplate.Infrastructure.Core.Common.Saving.DomainEvents;

public interface IDomainEventSource
{
    bool IsStagedOn(DbContext context);

    IReadOnlyCollection<IDomainEvent> DrainDomainEvents();

    void Restore(IEnumerable<IDomainEvent> domainEvents);
}
