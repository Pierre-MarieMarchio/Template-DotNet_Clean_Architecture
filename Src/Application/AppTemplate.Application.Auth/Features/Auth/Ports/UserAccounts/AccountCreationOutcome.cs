namespace AppTemplate.Application.Auth.Features.Auth.Ports.UserAccounts;

/// <param name="RejectionMessage">
/// Describes the submitted values, never the account store, so it is safe to return verbatim.
/// Set only for <see cref="AccountCreationStatus.Rejected"/>.
/// </param>
public sealed record AccountCreationOutcome(AccountCreationStatus Status, Guid UserId, string? RejectionMessage)
{
    /// <summary>The user name or the address is taken, and the caller is not told which.</summary>
    public static AccountCreationOutcome Conflict { get; } = new(AccountCreationStatus.Conflict, Guid.Empty, null);

    /// <summary>The account exists, and its id is the caller's to mint a confirmation for.</summary>
    public static AccountCreationOutcome Created(Guid userId) => new(AccountCreationStatus.Created, userId, null);

    /// <summary>The store refused the submitted values themselves.</summary>
    public static AccountCreationOutcome Rejected(string message) =>
        new(AccountCreationStatus.Rejected, Guid.Empty, message);
}
