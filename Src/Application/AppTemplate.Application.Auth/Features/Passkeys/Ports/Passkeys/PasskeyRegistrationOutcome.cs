namespace AppTemplate.Application.Auth.Features.Passkeys.Ports.Passkeys;

/// <summary>What completing a registration ended with: the credential as stored, when it ended
/// well, and otherwise the <see cref="PasskeyRegistrationStatus"/> that names why.</summary>
/// <param name="Credential">The stored credential, or <c>null</c> on any status but
/// <see cref="PasskeyRegistrationStatus.Registered"/>.</param>
public sealed record PasskeyRegistrationOutcome(PasskeyRegistrationStatus Status, PasskeyCredential? Credential = null)
{
    /// <summary>The credential as stored, with the name its holder gave it.</summary>
    public static PasskeyRegistrationOutcome Registered(PasskeyCredential credential) =>
        new(PasskeyRegistrationStatus.Registered, credential);

    /// <summary>Any of the refusals that collapse into one value — see the enum.</summary>
    public static PasskeyRegistrationOutcome InvalidCeremony() =>
        new(PasskeyRegistrationStatus.InvalidCeremony);

    /// <summary>The account the caller holds a token for stopped existing mid-ceremony.</summary>
    public static PasskeyRegistrationOutcome NoSuchAccount() =>
        new(PasskeyRegistrationStatus.NoSuchAccount);

    /// <summary>The store would not hold the verified credential.</summary>
    public static PasskeyRegistrationOutcome StoreRejected() =>
        new(PasskeyRegistrationStatus.StoreRejected);
}
