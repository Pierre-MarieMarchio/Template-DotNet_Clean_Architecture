namespace AppTemplate.Application.Auth.Features.Passkeys.Ports.Passkeys;

/// <summary>How completing a passkey sign-in ended.</summary>
public enum PasskeySignInStatus
{
    /// <summary>
    /// The assertion verified against the ceremony's challenge and names an account the store
    /// holds, and the credential's new signature counter has been persisted.
    /// </summary>
    Authenticated,

    /// <summary>
    /// The ceremony did not verify — a state string this adapter never issued, an assertion that
    /// does not answer its challenge, a signature the stored public key refuses, or a signature
    /// counter that did not advance (the clone signal). One value for all of them: the caller can
    /// do nothing with the distinction, and an anonymous caller must not be told which part of a
    /// guess was wrong.
    /// </summary>
    InvalidCeremony,

    /// <summary>The assertion verified but the store refused to persist the credential's new
    /// signature counter. Sign-in is refused rather than completed against a credential whose
    /// replay window would stay open.</summary>
    StoreRejected,
}
