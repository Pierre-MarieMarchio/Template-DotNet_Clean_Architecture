namespace AppTemplate.Application.Auth.Features.Auth.Ports.EmailChangeTokens;

/// <summary>How redeeming an email-change token ended.</summary>
public enum EmailChangeConfirmationStatus
{
    /// <summary>The address on the account is now the one the token was issued for.</summary>
    Changed,

    /// <summary>
    /// The caller already authenticated as this id, so an absent account is not an enumeration
    /// concern here — see <see cref="IEmailChangeTokensService"/>. It only means the account was deleted
    /// after the token was issued.
    /// </summary>
    NoSuchAccount,

    /// <summary>Unknown, expired, already used, or issued for a different address.</summary>
    InvalidToken,

    /// <summary>The token was valid; the store refused the new address itself.</summary>
    Rejected,
}
