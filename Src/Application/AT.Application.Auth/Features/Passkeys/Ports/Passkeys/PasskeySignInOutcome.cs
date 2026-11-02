using AppTemplate.Application.Auth.Features.Accounts.Ports.Accounts;

namespace AppTemplate.Application.Auth.Features.Passkeys.Ports.Passkeys;

public sealed record PasskeySignInOutcome(PasskeySignInStatus Status, AccountIdentity? Account = null)
{
    public static PasskeySignInOutcome Authenticated(AccountIdentity account) =>
        new(PasskeySignInStatus.Authenticated, account);

    public static PasskeySignInOutcome InvalidCeremony() =>
        new(PasskeySignInStatus.InvalidCeremony);

    public static PasskeySignInOutcome StoreRejected() =>
        new(PasskeySignInStatus.StoreRejected);
}
