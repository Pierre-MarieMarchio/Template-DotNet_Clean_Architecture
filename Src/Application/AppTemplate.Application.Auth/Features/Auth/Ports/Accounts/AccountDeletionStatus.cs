namespace AppTemplate.Application.Auth.Features.Auth.Ports.Accounts;

/// <summary>The closed set of ways an account deletion can end.</summary>
public enum AccountDeletionStatus
{
    /// <summary>The account row and what the user store holds for it are gone.</summary>
    Deleted,

    /// <summary>The account was already gone when the call reached the store.</summary>
    NoSuchAccount,

    /// <summary>The store refused the deletion itself. Nothing was committed.</summary>
    Rejected,
}
