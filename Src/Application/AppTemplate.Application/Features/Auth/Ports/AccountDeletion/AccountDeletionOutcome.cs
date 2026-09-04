namespace AppTemplate.Application.Features.Auth.Ports.AccountDeletion;

public enum AccountDeletionOutcome
{
    Deleted,

    NoSuchAccount,

    /// <summary>The account was found but the store refused to delete it.</summary>
    Rejected,
}
