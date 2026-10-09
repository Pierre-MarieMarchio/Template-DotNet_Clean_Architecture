namespace AppTemplate.Application.Auth.Features.Auth.Ports.TwoFactor;

/// <summary>How turning an account's own second factor off ended.</summary>
public enum TwoFactorDisableStatus
{
    /// <summary>The account no longer demands a code at sign-in.</summary>
    Disabled,

    /// <summary>The supplied current password did not match the one on file.</summary>
    IncorrectPassword,

    /// <summary>
    /// The password matched, but the store refused the change, so the second factor is still on.
    /// Describes the store's state, never anything the caller submitted.
    /// </summary>
    Rejected,

    /// <summary>
    /// The caller's token names an account that no longer exists: it was deleted after the token
    /// was issued.
    /// </summary>
    NoSuchAccount,
}
