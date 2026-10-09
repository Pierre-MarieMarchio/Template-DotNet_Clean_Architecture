using AppTemplate.Application.Features.ExternalIdentity.Services;
using AppTemplate.Application.Features.Reminders.Ports.ReminderRecipients;
using AppTemplate.Domain.Core.Common.Primitives;

namespace AppTemplate.Api.Features.Reminders.Directories;

/// <summary>
/// <see cref="IReminderRecipientDirectory"/> for callers authenticated by an external provider. A
/// reminder rings in a background pass, which has no token to read claims from, so the address and
/// name are those the provider last sent, kept on the owner's mapping row.
/// </summary>
/// <remarks>
/// No address, no recipient: an owner whose provider never sent one is skipped rather than guessed
/// at. A missing name falls back to the address, which is what the greeting can still use.
/// </remarks>
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
