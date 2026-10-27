using AppTemplate.Application.Auth.Features.Accounts.Ports.Accounts;
using AppTemplate.Application.Features.Reminders.Ports.ReminderRecipients;
using AppTemplate.Domain.Core.Common.Primitives;

namespace AppTemplate.PresentationApp.Features.Reminders.Composition;

internal sealed class AccountReminderRecipientDirectory(IAccountsService accounts) : IReminderRecipientDirectory
{
    public async Task<ReminderRecipient?> FindAsync(UserId ownerId, CancellationToken cancellationToken = default)
    {
        var profile = await accounts.FindProfileAsync(ownerId.Value, cancellationToken);

        return profile is null ? null : new ReminderRecipient(profile.Email, profile.Email);
    }
}
