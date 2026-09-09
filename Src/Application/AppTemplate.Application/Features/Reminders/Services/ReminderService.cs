using AppTemplate.Application.Core.Common.Concurrency;
using AppTemplate.Application.Core.Common.Ownership;
using AppTemplate.Application.Core.Common.Ports;
using AppTemplate.Application.Core.Common.Results;
using AppTemplate.Application.Features.Reminders.Errors;
using AppTemplate.Domain.Features.Reminders.Entities;
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

        return OwnedAggregate.Require(
            await repository.GetAsync(reminderId, cancellationToken),
            userId.Value,
            ReminderErrors.ReminderNotFound(reminderId),
            precondition);
    }
}
