namespace AppTemplate.Application.Auth.Features.Auth.Ports.Accounts;

/// <summary>What <c>IAccountsService.DeleteAsync</c> hands back: the status alone, with nothing to add to it.</summary>
public sealed record AccountDeletionOutcome(AccountDeletionStatus Status)
{
    /// <summary>The account row and what the user store holds for it are gone.</summary>
    public static AccountDeletionOutcome Deleted { get; } = new(AccountDeletionStatus.Deleted);

    /// <summary>The account was already gone when the call reached the store.</summary>
    public static AccountDeletionOutcome NoSuchAccount { get; } = new(AccountDeletionStatus.NoSuchAccount);

    /// <summary>The store refused the deletion itself. Nothing was committed.</summary>
    public static AccountDeletionOutcome Rejected { get; } = new(AccountDeletionStatus.Rejected);
}
