namespace AppTemplate.Application.Auth.Features.Accounts.Ports.Accounts;

/// <summary>How changing a password from the account's own request ended.</summary>
public enum PasswordChangeStatus
{
    /// <summary>The password on file is the new one.</summary>
    Changed,

    /// <summary>The supplied current password did not match the one on file.</summary>
    IncorrectCurrentPassword,

    /// <summary>The current password matched, but the store refused the new one itself.</summary>
    Rejected,
    /// <summary>
    /// The caller's token names an account that no longer exists: it was deleted after the token
    /// was issued.
    /// </summary>
    NoSuchAccount,
}
