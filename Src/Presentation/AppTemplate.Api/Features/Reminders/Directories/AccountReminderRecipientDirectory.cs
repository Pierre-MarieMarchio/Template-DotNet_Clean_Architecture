using AppTemplate.Application.Auth.Features.Auth.Ports.Accounts;
using AppTemplate.Application.Features.Reminders.Ports.ReminderRecipients;
using AppTemplate.Domain.Core.Common.Primitives;

namespace AppTemplate.Api.Features.Reminders.Directories;

/// <summary>
/// <see cref="IReminderRecipientDirectory"/> over the local accounts. It lives in the host, the one
/// place that knows both features: Reminders names no identity module and authentication names no
/// reminder, so either can be generated without the other.
/// </summary>
internal sealed class AccountReminderRecipientDirectory(IAccountsService accounts) : IReminderRecipientDirectory
{
    public async Task<ReminderRecipient?> FindAsync(UserId ownerId, CancellationToken cancellationToken = default)
    {
        var profile = await accounts.FindProfileAsync(ownerId.Value, cancellationToken);

        return profile is null ? null : new ReminderRecipient(profile.UserName, profile.Email);
    }
}
