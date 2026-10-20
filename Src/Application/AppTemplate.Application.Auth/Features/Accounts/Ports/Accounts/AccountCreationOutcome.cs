namespace AppTemplate.Application.Auth.Features.Accounts.Ports.Accounts;

/// <param name="EmailRejection">Why the store refused the address, if it did.</param>
/// <param name="PasswordRejection">
/// Why the store refused the password, if it did. Each of the two describes the submitted value,
/// never the account store, so it is safe to return verbatim; set only for
/// <see cref="AccountCreationStatus.Rejected"/>, and at least one of them is then.
/// </param>
public sealed record AccountCreationOutcome(
    AccountCreationStatus Status,
    Guid UserId,
    string? EmailRejection = null,
    string? PasswordRejection = null)
{
    /// <summary>The address is taken.</summary>
    public static AccountCreationOutcome Conflict { get; } = new(AccountCreationStatus.Conflict, Guid.Empty);

    /// <summary>The account exists, and its id is the caller's to mint a confirmation for.</summary>
    public static AccountCreationOutcome Created(Guid userId) => new(AccountCreationStatus.Created, userId);

    /// <summary>The store refused the submitted values themselves, each reason against its own value.</summary>
    public static AccountCreationOutcome Rejected(
        string? emailRejection,
        string? passwordRejection) =>
        new(AccountCreationStatus.Rejected, Guid.Empty, emailRejection, passwordRejection);
}
