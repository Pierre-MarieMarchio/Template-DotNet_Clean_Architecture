namespace AppTemplate.Application.Auth.Features.Passkeys.Ports.Passkeys;

public sealed record PasskeyRegistrationOutcome(PasskeyRegistrationStatus Status, PasskeyCredential? Credential = null)
{
    public static PasskeyRegistrationOutcome Registered(PasskeyCredential credential) =>
        new(PasskeyRegistrationStatus.Registered, credential);

    public static PasskeyRegistrationOutcome InvalidCeremony() =>
        new(PasskeyRegistrationStatus.InvalidCeremony);

    public static PasskeyRegistrationOutcome NoSuchAccount() =>
        new(PasskeyRegistrationStatus.NoSuchAccount);

    public static PasskeyRegistrationOutcome StoreRejected() =>
        new(PasskeyRegistrationStatus.StoreRejected);
}
