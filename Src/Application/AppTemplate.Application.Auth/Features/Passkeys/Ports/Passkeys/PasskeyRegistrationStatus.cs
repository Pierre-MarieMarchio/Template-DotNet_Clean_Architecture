namespace AppTemplate.Application.Auth.Features.Passkeys.Ports.Passkeys;

public enum PasskeyRegistrationStatus
{
    Registered,

    InvalidCeremony,

    NoSuchAccount,

    StoreRejected,
}
