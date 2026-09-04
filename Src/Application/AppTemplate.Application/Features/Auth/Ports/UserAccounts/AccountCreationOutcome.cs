namespace AppTemplate.Application.Features.Auth.Ports.UserAccounts;

/// <param name="RejectionMessage">
/// Describes the submitted values, never the account store, so it is safe to return verbatim.
/// Set only for <see cref="AccountCreationStatus.Rejected"/>.
/// </param>
public sealed record AccountCreationOutcome(AccountCreationStatus Status, Guid UserId, string? RejectionMessage)
{
    public static AccountCreationOutcome Conflict { get; } = new(AccountCreationStatus.Conflict, Guid.Empty, null);

    public static AccountCreationOutcome Created(Guid userId) => new(AccountCreationStatus.Created, userId, null);

    public static AccountCreationOutcome Rejected(string message) =>
        new(AccountCreationStatus.Rejected, Guid.Empty, message);
}
