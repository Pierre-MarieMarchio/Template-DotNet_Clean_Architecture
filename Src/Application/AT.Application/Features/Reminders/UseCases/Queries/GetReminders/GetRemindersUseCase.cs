using AppTemplate.Application.Core.Common.Ports;
using AppTemplate.Application.Core.Common.Results;
using AppTemplate.Application.Core.Common.Validation;
using AppTemplate.Application.Features.Reminders.Model.Dtos;
using AppTemplate.Application.Features.Reminders.Ports.ReminderQueries;
using FluentValidation;

namespace AppTemplate.Application.Features.Reminders.UseCases.Queries.GetReminders;

public sealed class GetRemindersUseCase(
    IReminderQueries queries,
    ICurrentUser currentUser,
    IValidator<GetRemindersQuery> validator) : IGetRemindersUseCase
{
    public async Task<Result<IReadOnlyList<ReminderDto>>> ExecuteAsync(
        GetRemindersQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        var validation = await validator.EnsureValidAsync(query, cancellationToken);

        if (validation.IsFailure)
        {
            return validation.To<IReadOnlyList<ReminderDto>>();
        }

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
