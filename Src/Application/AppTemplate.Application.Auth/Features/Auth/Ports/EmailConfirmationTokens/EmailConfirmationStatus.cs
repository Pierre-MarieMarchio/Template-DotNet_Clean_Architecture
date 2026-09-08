namespace AppTemplate.Application.Auth.Features.Auth.Ports.EmailConfirmationTokens;

/// <summary>
/// Every value other than <see cref="Confirmed"/> must reach the caller as the same error: an
/// endpoint that distinguishes "no such address" from "wrong token" answers "is this address
/// registered?" for anybody holding a junk token.
/// </summary>
public enum EmailConfirmationStatus
{
    /// <summary>The address is proven, and sign-in is no longer refused for want of it.</summary>
    Confirmed,

    /// <summary>
    /// No account holds that address. Reaches the caller as the same error <see cref="InvalidToken"/> does.
    /// </summary>
    NoSuchAccount,

    /// <summary>Unknown, expired, already used, or issued for a different address.</summary>
    InvalidToken,
}
