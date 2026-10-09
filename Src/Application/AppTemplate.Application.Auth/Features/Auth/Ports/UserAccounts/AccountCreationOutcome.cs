namespace AppTemplate.Application.Auth.Features.Auth.Ports.UserAccounts;

/// <param name="UserNameRejection">Why the store refused the user name, if it did.</param>
/// <param name="EmailRejection">Why the store refused the address, if it did.</param>
/// <param name="PasswordRejection">
/// Why the store refused the password, if it did. Each of the three describes the submitted value,
/// never the account store, so it is safe to return verbatim; set only for
/// <see cref="AccountCreationStatus.Rejected"/>, and at least one of them is then.
/// </param>
public sealed record AccountCreationOutcome(
    AccountCreationStatus Status,
    Guid UserId,
    string? UserNameRejection = null,
    string? EmailRejection = null,
    string? PasswordRejection = null)
{
    /// <summary>The user name or the address is taken, and the caller is not told which.</summary>
    public static AccountCreationOutcome Conflict { get; } = new(AccountCreationStatus.Conflict, Guid.Empty);

    /// <summary>The account exists, and its id is the caller's to mint a confirmation for.</summary>
    public static AccountCreationOutcome Created(Guid userId) => new(AccountCreationStatus.Created, userId);

    /// <summary>The store refused the submitted values themselves, each reason against its own value.</summary>
    public static AccountCreationOutcome Rejected(
        string? userNameRejection,
        string? emailRejection,
        string? passwordRejection) =>
        new(AccountCreationStatus.Rejected, Guid.Empty, userNameRejection, emailRejection, passwordRejection);
}
