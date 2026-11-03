using AppTemplate.Application.Core.Common.Concurrency;
using AppTemplate.Application.Core.Common.Ports;
using AppTemplate.Application.Core.Common.Results;
using AppTemplate.Application.Features.Reminders.Model.Errors;
using AppTemplate.Domain.Features.Reminders.Model.Entities;
using AppTemplate.Domain.Features.Reminders.Repositories;

namespace AppTemplate.Application.Features.Reminders.Services;

internal sealed class ReminderService(IReminderRepository repository, ICurrentUser currentUser) : IReminderService
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

        var aggregate = await repository.GetAsync(userId.Value, reminderId, cancellationToken);

        if (aggregate is null)
        {
            return Result.Failure<Reminder>(ReminderErrors.ReminderNotFound(reminderId));
        }

        return precondition is not null && !precondition.IsSatisfiedBy(aggregate.Version)
            ? Result.Failure<Reminder>(ConcurrencyErrors.PreconditionFailed)
            : Result.Success(aggregate);
    }
}
