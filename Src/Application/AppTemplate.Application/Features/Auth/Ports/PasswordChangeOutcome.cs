namespace AppTemplate.Application.Features.Auth.Ports;

public enum PasswordChangeOutcome
{
    Changed,

    /// <summary>The supplied current password did not match the one on file.</summary>
    IncorrectCurrentPassword,

    /// <summary>The current password matched, but the store refused the new one itself.</summary>
    Rejected,
}
