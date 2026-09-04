using AppTemplate.Application.Features.Auth.Ports.EmailConfirmationTokens;

namespace AppTemplate.Application.Features.Auth.Ports.PasswordResetTokens;

/// <summary>
/// Every value other than <see cref="Reset"/> and <see cref="Rejected"/> must reach the caller as the
/// same error, for the reason given on <see cref="EmailConfirmationOutcome"/>.
/// </summary>
public enum PasswordResetOutcome
{
    Reset,

    NoSuchAccount,

    /// <summary>Unknown, expired, already used, or issued for a different address.</summary>
    InvalidToken,

    /// <summary>The token was valid; the store refused the new password itself.</summary>
    Rejected,
}
