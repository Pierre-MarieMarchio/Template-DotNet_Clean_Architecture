using AppTemplate.Application.Common.Concurrency;
using AppTemplate.Application.Common.Results;
using AppTemplate.Application.Features.Reminders.Errors;
using AppTemplate.Domain.Features.Reminders.Entities;

namespace AppTemplate.Application.Features.Reminders.Services;

/// <summary>
/// The one gate every reminder command loads its aggregate through, on the same model as
/// <c>ITodoListService</c>: identity, ownership and the version precondition, in that order.
/// </summary>
public interface IReminderService
{
    /// <returns>
    /// The aggregate, or a failure — <see cref="ReminderErrors.ReminderNotFound"/> for an
    /// anonymous caller, an unknown id or somebody else's reminder, and
    /// <see cref="ConcurrencyErrors.PreconditionFailed"/> once ownership is established but the
    /// caller named a version the aggregate no longer holds.
    /// </returns>
    Task<Result<Reminder>> LoadOwnedAsync(
        Guid reminderId,
        VersionPrecondition? precondition,
        CancellationToken cancellationToken = default);
}
