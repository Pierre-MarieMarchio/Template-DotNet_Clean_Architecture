using AppTemplate.Domain.Core.Common.Primitives;

namespace AppTemplate.Application.Features.Reminders.Ports.ReminderRecipients;

public interface IReminderRecipientDirectory
{
    Task<ReminderRecipient?> FindAsync(UserId ownerId, CancellationToken cancellationToken = default);
}
