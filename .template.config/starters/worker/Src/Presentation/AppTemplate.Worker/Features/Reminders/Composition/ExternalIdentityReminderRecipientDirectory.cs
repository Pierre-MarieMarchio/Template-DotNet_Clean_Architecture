using AppTemplate.Application.Features.ExternalIdentity.Services;
using AppTemplate.Application.Features.Reminders.Ports.ReminderRecipients;
using AppTemplate.Domain.Core.Common.Primitives;

namespace AppTemplate.Worker.Features.Reminders.Composition;

internal sealed class ExternalIdentityReminderRecipientDirectory(IExternalIdentityService identities)
    : IReminderRecipientDirectory
{
    public async Task<ReminderRecipient?> FindAsync(UserId ownerId, CancellationToken cancellationToken = default)
    {
        var profile = await identities.FindProfileAsync(ownerId, cancellationToken);

        if (profile?.Email is not { Length: > 0 } email)
        {
            return null;
        }

        return new ReminderRecipient(
            string.IsNullOrWhiteSpace(profile.DisplayName) ? email : profile.DisplayName,
            email);
    }
}
