namespace AppTemplate.Application.Auth.Features.Auth.Ports.UserAccounts;

/// <summary>How changing a password from the account's own request ended.</summary>
public enum PasswordChangeStatus
{
    /// <summary>The password on file is the new one.</summary>
    Changed,

    /// <summary>The supplied current password did not match the one on file.</summary>
    IncorrectCurrentPassword,

    /// <summary>The current password matched, but the store refused the new one itself.</summary>
    Rejected,
}
