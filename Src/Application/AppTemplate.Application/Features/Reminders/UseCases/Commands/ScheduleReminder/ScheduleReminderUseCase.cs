using AppTemplate.Application.Common;
using AppTemplate.Application.Common.Abstractions;
using AppTemplate.Application.Common.Validation;
using AppTemplate.Application.Features.Reminders.Dtos;
using AppTemplate.Application.Features.Reminders.Errors;
using AppTemplate.Application.Features.Reminders.Mapping;
using AppTemplate.Application.Features.TodoLists.Ports.TodoListQueries;
using AppTemplate.Domain.Features.Reminders.Entities;
using AppTemplate.Domain.Features.Reminders.Repositories;
using FluentValidation;

namespace AppTemplate.Application.Features.Reminders.UseCases.Commands.ScheduleReminder;

/// <summary>
/// Reaches into <c>TodoLists</c>' own read port to confirm the item is real and belongs to the
/// caller before a reminder is created for it — <see cref="Reminder.Schedule"/> trusts every id
/// it is handed, so this is the only place that check happens.
/// </summary>
public sealed class ScheduleReminderUseCase(
    ITodoListQueries todoLists,
    IReminderRepository reminders,
    IUnitOfWork unitOfWork,
    ICurrentUser currentUser,
    IDateTimeProvider dateTimeProvider,
    IValidator<ScheduleReminderCommand> validator) : IScheduleReminderUseCase
{
    public async Task<Result<Versioned<ReminderDto>>> ExecuteAsync(
        ScheduleReminderCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        var validation = await validator.EnsureValidAsync(command, cancellationToken);

        if (validation.IsFailure)
        {
            return validation.To<Versioned<ReminderDto>>();
        }

        var userId = currentUser.RequireUserId();

        if (userId.IsFailure)
        {
            return userId.To<Versioned<ReminderDto>>();
        }

        var list = await todoLists.GetDetailAsync(command.TodoListId, userId.Value, cancellationToken);
        var item = list?.Value.Items.FirstOrDefault(candidate => candidate.Id == command.TodoItemId);

        if (item is null)
        {
            return Result.Failure<Versioned<ReminderDto>>(ReminderErrors.TargetNotFound(command.TodoItemId));
        }

        // Caught: whether the due date is in the past depends on the clock at the moment of the
        // call, not on anything the validator above could have checked statically.
        var scheduled = DomainGuard.Try(() => Reminder.Schedule(
            userId.Value,
            command.TodoListId,
            command.TodoItemId,
            command.DueAt,
            dateTimeProvider.UtcNow));

        if (scheduled.IsFailure)
        {
            return scheduled.To<Versioned<ReminderDto>>();
        }

        reminders.Add(scheduled.Value);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return ReminderProjection.ToVersioned(scheduled.Value);
    }
}
