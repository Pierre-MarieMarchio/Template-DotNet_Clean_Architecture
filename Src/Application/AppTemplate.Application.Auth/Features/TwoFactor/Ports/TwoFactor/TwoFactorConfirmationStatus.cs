namespace AppTemplate.Application.Auth.Features.TwoFactor.Ports.TwoFactor;

/// <summary>How confirming a pending enrollment with a first code ended.</summary>
public enum TwoFactorConfirmationStatus
{
    /// <summary>Two-factor sign-in is on and a fresh set of recovery codes exists.</summary>
    Confirmed,

    /// <summary>
    /// The code did not match — including when no pending secret exists at all, which
    /// <c>BeginAsync</c> should have been called to provision first.
    /// </summary>
    InvalidCode,

    /// <summary>The supplied current password did not match the one on file.</summary>
    IncorrectPassword,

    /// <summary>
    /// The caller's token names an account that no longer exists: it was deleted after the token
    /// was issued.
    /// </summary>
    NoSuchAccount,

    /// <summary>
    /// Two-factor sign-in is already on. Nothing was checked or written: the recovery codes on file
    /// stay the ones the caller was shown.
    /// </summary>
    AlreadyEnabled,

    /// <summary>
    /// Password and code both matched, but the store refused to arm the second factor, so it stays
    /// off. Describes the store's state, never anything the caller submitted.
    /// </summary>
    Rejected,
}
