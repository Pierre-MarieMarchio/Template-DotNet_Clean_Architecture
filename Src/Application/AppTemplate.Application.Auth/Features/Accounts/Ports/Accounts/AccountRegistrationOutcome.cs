namespace AppTemplate.Application.Auth.Features.Accounts.Ports.Accounts;

/// <summary>What registering an address ended with, as the store saw it.</summary>
/// <param name="Status">Which of the three ways it ended.</param>
/// <param name="UserId">The new account's id; set only for <see cref="AccountRegistrationStatus.Created"/>.</param>
/// <param name="EmailConfirmed">Whether the account already holding the address has confirmed it.</param>
/// <param name="EmailRejection">The store's refusal of the submitted address, safe to return verbatim.</param>
/// <param name="PasswordRejection">The store's refusal of the submitted password, safe to return verbatim.</param>
public sealed record AccountRegistrationOutcome(
    AccountRegistrationStatus Status,
    Guid UserId,
    bool EmailConfirmed = false,
    string? EmailRejection = null,
    string? PasswordRejection = null)
{
    /// <summary>A new, unconfirmed account holds the address.</summary>
    public static AccountRegistrationOutcome Created(Guid userId) => new(AccountRegistrationStatus.Created, userId);

    /// <summary>An existing account already holds the address and was left unchanged.</summary>
    public static AccountRegistrationOutcome AlreadyRegistered(bool emailConfirmed) =>
        new(AccountRegistrationStatus.AlreadyRegistered, Guid.Empty, emailConfirmed);

    /// <summary>The submitted values broke the store's rules; nothing was looked up or created.</summary>
    public static AccountRegistrationOutcome Rejected(string? emailRejection, string? passwordRejection) =>
        new(
            AccountRegistrationStatus.Rejected,
            Guid.Empty,
            EmailRejection: emailRejection,
            PasswordRejection: passwordRejection);
}
