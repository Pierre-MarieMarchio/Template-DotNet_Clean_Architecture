namespace AppTemplate.Application.Auth.Features.Auth.Ports.EmailTokens;

/// <summary>How redeeming a password-reset token ended.</summary>
public enum PasswordResetStatus
{
    /// <summary>The password on file is the new one, and the token that authorised it is spent.</summary>
    Reset,

    /// <summary>
    /// No account holds that address, or the token is unknown, expired, already used or issued for a
    /// different address. One value, so no caller can tell an unknown address from a bad token.
    /// </summary>
    InvalidToken,

    /// <summary>The token was valid; the store refused the new password itself.</summary>
    Rejected,
}
