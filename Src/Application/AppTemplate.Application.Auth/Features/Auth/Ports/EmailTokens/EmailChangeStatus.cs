namespace AppTemplate.Application.Auth.Features.Auth.Ports.EmailTokens;

/// <summary>How redeeming an email-change token ended.</summary>
public enum EmailChangeStatus
{
    /// <summary>The address on the account is now the one the token was issued for.</summary>
    Changed,

    /// <summary>
    /// The token is unknown, expired, already used or issued for a different address — or the
    /// account was deleted after it was issued, which the caller answers the same way.
    /// </summary>
    InvalidToken,

    /// <summary>The token was valid; the store refused the new address itself.</summary>
    Rejected,
}
