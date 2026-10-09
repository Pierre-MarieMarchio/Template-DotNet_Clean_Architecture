using AppTemplate.Domain.Core.Common.Primitives;

namespace AppTemplate.Application.Features.Reminders.Ports.ReminderRecipients;

/// <summary>
/// Who a reminder rings: the owner's name and address, resolved when the reminder fires rather than
/// stored with it, so a renamed or moved account is reached where it is now.
/// <para>
/// Reminders owns this contract and names no identity module. The host supplies the adapter: over
/// the local accounts when the template keeps its own, and from what an external identity
/// provider says when it does not.
/// </para>
/// </summary>
public interface IReminderRecipientDirectory
{
    /// <returns><c>null</c> when the owner no longer names an account.</returns>
    Task<ReminderRecipient?> FindAsync(UserId ownerId, CancellationToken cancellationToken = default);
}
