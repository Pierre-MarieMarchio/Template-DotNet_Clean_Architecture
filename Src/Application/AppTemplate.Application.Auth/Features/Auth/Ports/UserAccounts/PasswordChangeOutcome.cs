namespace AppTemplate.Application.Auth.Features.Auth.Ports.UserAccounts;

/// <param name="RejectionMessage">
/// Describes the submitted password, never the account store, so it is safe to return verbatim. Set
/// only for <see cref="PasswordChangeStatus.Rejected"/>.
/// </param>
public sealed record PasswordChangeOutcome(PasswordChangeStatus Status, string? RejectionMessage = null)
{
    /// <summary>The password on file is the new one.</summary>
    public static PasswordChangeOutcome Changed { get; } = new(PasswordChangeStatus.Changed);

    /// <summary>The supplied current password did not match, so nothing was replaced.</summary>
    public static PasswordChangeOutcome IncorrectCurrentPassword { get; } =
        new(PasswordChangeStatus.IncorrectCurrentPassword);

    /// <summary>The current password matched; the store refused the new one itself.</summary>
    public static PasswordChangeOutcome Rejected(string message) => new(PasswordChangeStatus.Rejected, message);
}
