using AppTemplate.Application.Auth.Features.Auth.Ports.EmailConfirmationTokens;

namespace AppTemplate.Application.Auth.Features.Auth.Ports.PasswordResetTokens;

/// <summary>
/// Every value other than <see cref="Reset"/> and <see cref="Rejected"/> must reach the caller as the
/// same error, for the reason given on <see cref="EmailConfirmationStatus"/>.
/// </summary>
public enum PasswordResetStatus
{
    /// <summary>The password on file is the new one, and the token that authorised it is spent.</summary>
    Reset,

    /// <summary>No account holds that address.</summary>
    NoSuchAccount,

    /// <summary>Unknown, expired, already used, or issued for a different address.</summary>
    InvalidToken,

    /// <summary>The token was valid; the store refused the new password itself.</summary>
    Rejected,
}
