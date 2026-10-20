namespace AppTemplate.Application.Auth.Features.Accounts.Ports.EmailTokens;

/// <summary>How a request to change an address ended.</summary>
public enum EmailChangeRequestStatus
{
    /// <summary>The supplied current password did not match the one on file.</summary>
    IncorrectCurrentPassword,

    /// <summary>The current password matched and a token was minted for the new address.</summary>
    Requested,

    /// <summary>The current password matched, and another account already holds the new address.</summary>
    AddressAlreadyRegistered,

    /// <summary>
    /// The caller's token names an account that no longer exists: it was deleted after the token
    /// was issued.
    /// </summary>
    NoSuchAccount,
}
