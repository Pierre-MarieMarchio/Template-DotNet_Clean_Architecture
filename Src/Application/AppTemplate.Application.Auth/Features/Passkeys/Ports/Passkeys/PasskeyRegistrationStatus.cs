namespace AppTemplate.Application.Auth.Features.Passkeys.Ports.Passkeys;

/// <summary>How completing a passkey registration ended.</summary>
public enum PasskeyRegistrationStatus
{
    /// <summary>The credential verified against the ceremony's challenge and was stored on the account.</summary>
    Registered,

    /// <summary>
    /// The ceremony did not verify: a state string this adapter never issued, a credential that
    /// does not answer it, a credential already registered to an account, or a state issued for a
    /// different account than the caller named. One value for all of them, since the caller cannot
    /// act differently on any.
    /// </summary>
    InvalidCeremony,

    /// <summary>The account no longer exists, checked at the moment of storing.</summary>
    NoSuchAccount,

    /// <summary>The ceremony verified, but the store refused to hold the credential.</summary>
    StoreRejected,
}
