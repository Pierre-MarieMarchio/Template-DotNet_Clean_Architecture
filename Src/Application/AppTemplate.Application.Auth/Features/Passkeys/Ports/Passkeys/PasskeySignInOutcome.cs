using AppTemplate.Application.Auth.Features.Accounts.Ports.Accounts;

namespace AppTemplate.Application.Auth.Features.Passkeys.Ports.Passkeys;

/// <summary>
/// What completing a sign-in ended with: the account the credential identified, when it ended
/// well, and otherwise the <see cref="PasskeySignInStatus"/> that names why. Which tokens the
/// account gets — whether it may still sign in at all — is not this outcome's to say.
/// </summary>
/// <param name="Account">The account the credential identified, or <c>null</c> on any status but
/// <see cref="PasskeySignInStatus.Authenticated"/>.</param>
public sealed record PasskeySignInOutcome(PasskeySignInStatus Status, AccountIdentity? Account = null)
{
    /// <summary>The account, with its current facts — read from the store, as at any sign-in.</summary>
    public static PasskeySignInOutcome Authenticated(AccountIdentity account) =>
        new(PasskeySignInStatus.Authenticated, account);

    /// <summary>Any of the refusals that collapse into one value — see the enum.</summary>
    public static PasskeySignInOutcome InvalidCeremony() =>
        new(PasskeySignInStatus.InvalidCeremony);

    /// <summary>The credential verified, but its counter could not be persisted, so sign-in stops here.</summary>
    public static PasskeySignInOutcome StoreRejected() =>
        new(PasskeySignInStatus.StoreRejected);
}
