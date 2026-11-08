using AppTemplate.Application.Core.Common.Concurrency;
using AppTemplate.Application.Core.Common.Results;
using AppTemplate.Domain.Features.Reminders.Model.Entities;

namespace AppTemplate.Application.Features.Reminders.Services;

public interface IReminderService
{
    Task<Result<Reminder>> LoadOwnedAsync(
        Guid reminderId,
        VersionPrecondition? precondition,
        CancellationToken cancellationToken = default);
}
