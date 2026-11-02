using AppTemplate.Application.Core.Common.Concurrency;
using AppTemplate.Application.Core.Common.Ports;
using AppTemplate.Application.Core.Common.Results;
using AppTemplate.Application.Core.Common.Validation;
using AppTemplate.Application.Features.Reminders.Model.Dtos;
using AppTemplate.Application.Features.Reminders.Model.Errors;
using AppTemplate.Application.Features.Reminders.Model.Mapping;
using AppTemplate.Application.Features.TodoLists.Ports.TodoItemQueries;
using AppTemplate.Domain.Features.Reminders.Model.Entities;
using AppTemplate.Domain.Features.Reminders.Repositories;
using Mediator;

namespace AppTemplate.Application.Features.Reminders.UseCases.Commands.ScheduleReminder;

public sealed record ScheduleReminderCommand(Guid TodoListId, Guid TodoItemId, DateTimeOffset DueAt) : ICommand<Result<Versioned<ReminderDto>>>;

public sealed class ScheduleReminderCommandHandler(
    ITodoItemQueries todoItems,
    IReminderRepository reminders,
    IUnitOfWork unitOfWork,
    ICurrentUser currentUser,
    IDateTimeProvider dateTimeProvider) : ICommandHandler<ScheduleReminderCommand, Result<Versioned<ReminderDto>>>
{
    public async ValueTask<Result<Versioned<ReminderDto>>> Handle(
        ScheduleReminderCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

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
