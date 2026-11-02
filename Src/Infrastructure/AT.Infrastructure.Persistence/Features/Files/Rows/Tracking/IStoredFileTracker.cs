using AppTemplate.Domain.Features.Files.Model.Entities;
using AppTemplate.Infrastructure.Core.Common.Saving.DomainEvents;
using AppTemplate.Infrastructure.Core.Common.Saving.Tracking;
using AppTemplate.Infrastructure.Persistence.Features.Files.Rows.Models;

namespace AppTemplate.Infrastructure.Persistence.Features.Files.Rows.Tracking;

internal interface IStoredFileTracker : IAggregateFlusher, IDomainEventSource
{
    StoredFile? Find(Guid id);

    void Track(StoredFile aggregate, StoredFileRecord record);

    StoredFileRecord? FindRecord(Guid id);

    void MarkRemoved(StoredFile aggregate, StoredFileRecord record);
}
