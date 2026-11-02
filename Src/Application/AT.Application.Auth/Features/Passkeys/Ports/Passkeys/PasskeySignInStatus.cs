namespace AppTemplate.Application.Auth.Features.Passkeys.Ports.Passkeys;

public enum PasskeySignInStatus
{
    Authenticated,

    InvalidCeremony,

    StoreRejected,
}
