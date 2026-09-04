namespace AppTemplate.Application.Features.Auth.Ports.EmailConfirmationTokens;

/// <summary>
/// Every value other than <see cref="Confirmed"/> must reach the caller as the same error: an
/// endpoint that distinguishes "no such address" from "wrong token" answers "is this address
/// registered?" for anybody holding a junk token.
/// </summary>
public enum EmailConfirmationStatus
{
    Confirmed,

    NoSuchAccount,

    /// <summary>Unknown, expired, already used, or issued for a different address.</summary>
    InvalidToken,
}
