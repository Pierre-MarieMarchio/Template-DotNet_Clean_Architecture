using AppTemplate.Application.Features.Reminders.Model.Dtos;
using AppTemplate.Domain.Core.Common.Primitives;

namespace AppTemplate.Application.Features.Reminders.Ports.ReminderQueries;

public interface IReminderQueries
{
    Task<IReadOnlyList<ReminderDto>> GetForTodoItemAsync(
        UserId ownerId,
        Guid todoListId,
        Guid todoItemId,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<UserId>> ListDataOwnerIdsAsync(CancellationToken cancellationToken = default);
}
