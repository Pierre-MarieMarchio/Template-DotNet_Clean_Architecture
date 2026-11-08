namespace AppTemplate.Application.Auth.Features.Accounts.Ports.Accounts;

public sealed record AccountDeletionOutcome(AccountDeletionStatus Status)
{
    public static AccountDeletionOutcome Deleted { get; } = new(AccountDeletionStatus.Deleted);

    public static AccountDeletionOutcome NoSuchAccount { get; } = new(AccountDeletionStatus.NoSuchAccount);

    public static AccountDeletionOutcome Rejected { get; } = new(AccountDeletionStatus.Rejected);
}
