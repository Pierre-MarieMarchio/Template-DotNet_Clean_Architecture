using AppTemplate.Application.Core.Common.Ports;
using AppTemplate.Application.Core.Common.Results;
using AppTemplate.Application.Features.Reminders.Model.Dtos;
using AppTemplate.Application.Features.Reminders.Ports.ReminderQueries;
using Mediator;

namespace AppTemplate.Application.Features.Reminders.UseCases.Queries.GetReminders;

public sealed record GetRemindersQuery(Guid TodoListId, Guid TodoItemId) : IQuery<Result<IReadOnlyList<ReminderDto>>>;

public sealed class GetRemindersQueryHandler(
    IReminderQueries queries,
    ICurrentUser currentUser) : IQueryHandler<GetRemindersQuery, Result<IReadOnlyList<ReminderDto>>>
{
    public async ValueTask<Result<IReadOnlyList<ReminderDto>>> Handle(
        GetRemindersQuery query,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        var userId = currentUser.RequireUserId();

        if (userId.IsFailure)
        {
            return userId.To<IReadOnlyList<ReminderDto>>();
        }

        var reminders = await queries.GetForTodoItemAsync(
            userId.Value,
            query.TodoListId,
            query.TodoItemId,
            cancellationToken);

        return Result.Success(reminders);
    }
}
