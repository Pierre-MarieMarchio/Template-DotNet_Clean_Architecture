namespace AppTemplate.Application.Auth.Features.Accounts.Ports.Accounts;

public sealed record PasswordChangeOutcome(PasswordChangeStatus Status, string? RejectionMessage = null)
{
    public static PasswordChangeOutcome Changed { get; } = new(PasswordChangeStatus.Changed);

    public static PasswordChangeOutcome IncorrectCurrentPassword { get; } =
        new(PasswordChangeStatus.IncorrectCurrentPassword);

    public static PasswordChangeOutcome NoSuchAccount { get; } = new(PasswordChangeStatus.NoSuchAccount);

    public static PasswordChangeOutcome Rejected(string message) => new(PasswordChangeStatus.Rejected, message);
}
