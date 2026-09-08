namespace AppTemplate.Application.Auth.Features.Auth.Ports.AccountDeletion;

/// <summary>How removing an account ended.</summary>
public enum AccountDeletionStatus
{
    /// <summary>The account row is gone.</summary>
    Deleted,

    /// <summary>No account has that id: already deleted, or deleted by another request first.</summary>
    NoSuchAccount,

    /// <summary>The account was found but the store refused to delete it.</summary>
    Rejected,
}
