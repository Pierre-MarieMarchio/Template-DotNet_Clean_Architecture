using AppTemplate.Application.Common;
using AppTemplate.Application.Common.Abstractions;
using AppTemplate.Application.Common.Concurrency;
using AppTemplate.Application.Features.Reminders.Errors;
using AppTemplate.Domain.Features.Reminders.Entities;
using AppTemplate.Domain.Features.Reminders.Repositories;

namespace AppTemplate.Application.Features.Reminders.Services;

internal sealed class ReminderAccess(IReminderRepository repository, ICurrentUser currentUser) : IReminderAccess
{
    public async Task<Result<Reminder>> LoadOwnedAsync(
        Guid reminderId,
        VersionPrecondition? precondition,
        CancellationToken cancellationToken = default)
    {
        var userId = currentUser.RequireUserId();

        if (userId.IsFailure)
        {
            return userId.To<Reminder>();
        }

        var ownerId = userId.Value;

        var reminder = await repository.GetAsync(reminderId, cancellationToken);

        if (reminder is null || reminder.OwnerId != ownerId)
        {
            return Result.Failure<Reminder>(ReminderErrors.ReminderNotFound(reminderId));
        }

        // Compared against the aggregate this call just loaded, so nothing can commit between the
        // comparison and whatever the caller does with the result.
        if (precondition is not null && !precondition.IsSatisfiedBy(reminder.Version))
        {
            return Result.Failure<Reminder>(ConcurrencyErrors.PreconditionFailed);
        }

        return reminder;
    }
}
