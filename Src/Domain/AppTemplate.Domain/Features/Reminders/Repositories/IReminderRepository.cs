using AppTemplate.Domain.Core.Common.Primitives;
using AppTemplate.Domain.Features.Reminders.Model.Entities;

namespace AppTemplate.Domain.Features.Reminders.Repositories;

public interface IReminderRepository
{
    Task<Reminder?> GetAsync(Guid id, CancellationToken cancellationToken);

    Task<IReadOnlyList<Reminder>> GetForTodoItemAsync(Guid todoItemId, CancellationToken cancellationToken);

    Task<IReadOnlyList<Reminder>> GetDueAsync(
        DateTimeOffset asOf,
        int batchSize,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<Reminder>> GetForOwnerAsync(
        UserId ownerId,
        CancellationToken cancellationToken);

    void Add(Reminder reminder);

    void Remove(Reminder reminder);
}
