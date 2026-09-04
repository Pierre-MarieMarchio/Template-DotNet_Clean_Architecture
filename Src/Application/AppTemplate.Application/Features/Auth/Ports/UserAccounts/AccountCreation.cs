namespace AppTemplate.Application.Features.Auth.Ports.UserAccounts;

/// <param name="RejectionMessage">
/// Describes the submitted values, never the account store, so it is safe to return verbatim.
/// Set only for <see cref="AccountCreationOutcome.Rejected"/>.
/// </param>
public sealed record AccountCreation(AccountCreationOutcome Outcome, Guid UserId, string? RejectionMessage)
{
    public static AccountCreation Conflict { get; } = new(AccountCreationOutcome.Conflict, Guid.Empty, null);

    public static AccountCreation Created(Guid userId) => new(AccountCreationOutcome.Created, userId, null);

    public static AccountCreation Rejected(string message) =>
        new(AccountCreationOutcome.Rejected, Guid.Empty, message);
}
