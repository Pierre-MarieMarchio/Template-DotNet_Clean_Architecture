using AppTemplate.Application.Core.Common.Concurrency;
using AppTemplate.Application.Core.Common.Ports;
using AppTemplate.Application.Core.Common.Results;
using AppTemplate.Application.Core.Common.Validation;
using AppTemplate.Application.Features.Reminders.Dtos;
using AppTemplate.Application.Features.Reminders.Errors;
using AppTemplate.Application.Features.Reminders.Mapping;
using AppTemplate.Application.Features.TodoLists.Ports.TodoItemQueries;
using AppTemplate.Domain.Features.Reminders.Entities;
using AppTemplate.Domain.Features.Reminders.Repositories;
using FluentValidation;

namespace AppTemplate.Application.Features.Reminders.UseCases.Commands.ScheduleReminder;

/// <summary>
/// Asks <c>TodoLists</c>' item port whether the item is real and belongs to the caller before a
/// reminder is created for it — <see cref="Reminder.Schedule"/> trusts every id it is handed, so
/// this is the only place that check happens.
/// <para>
/// <b>Guarantees.</b> One transaction inserts the reminder, and a new row has no concurrency token
/// to lose on. The use case is not idempotent by itself — a repeated call schedules a second
/// reminder — and the route makes it safe to retry with <c>[Idempotent]</c> and an
/// <c>Idempotency-Key</c>. The item can be removed between the check and the insert; the reminder
/// is then orphaned and retired by the next firing pass, as one whose item is removed later is.
/// </para>
/// </summary>
public sealed class ScheduleReminderUseCase(
    ITodoItemQueries todoItems,
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

        bool itemExists = await todoItems.ExistsForOwnerAsync(
            command.TodoListId,
            command.TodoItemId,
            userId.Value,
            cancellationToken);

        if (!itemExists)
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

        return ReminderDtoMapping.ToVersioned(scheduled.Value);
    }
}
