namespace AppTemplate.Application.Features.Auth.Ports.UserAccounts;

/// <param name="RejectionMessage">
/// Describes the submitted password, never the account store, so it is safe to return verbatim. Set
/// only for <see cref="PasswordChangeStatus.Rejected"/>.
/// </param>
public sealed record PasswordChangeOutcome(PasswordChangeStatus Status, string? RejectionMessage = null)
{
    public static PasswordChangeOutcome Changed { get; } = new(PasswordChangeStatus.Changed);

    public static PasswordChangeOutcome IncorrectCurrentPassword { get; } =
        new(PasswordChangeStatus.IncorrectCurrentPassword);

    public static PasswordChangeOutcome Rejected(string message) => new(PasswordChangeStatus.Rejected, message);
}
