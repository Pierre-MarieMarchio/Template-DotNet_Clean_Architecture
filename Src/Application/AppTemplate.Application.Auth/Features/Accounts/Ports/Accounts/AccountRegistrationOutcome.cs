namespace AppTemplate.Application.Auth.Features.Accounts.Ports.Accounts;

public sealed record AccountRegistrationOutcome(
    AccountRegistrationStatus Status,
    Guid UserId,
    bool EmailConfirmed = false,
    string? EmailRejection = null,
    string? PasswordRejection = null)
{
    public static AccountRegistrationOutcome Created(Guid userId) => new(AccountRegistrationStatus.Created, userId);

    public static AccountRegistrationOutcome AlreadyRegistered(bool emailConfirmed) =>
        new(AccountRegistrationStatus.AlreadyRegistered, Guid.Empty, emailConfirmed);

    public static AccountRegistrationOutcome Rejected(string? emailRejection, string? passwordRejection) =>
        new(
            AccountRegistrationStatus.Rejected,
            Guid.Empty,
            EmailRejection: emailRejection,
            PasswordRejection: passwordRejection);
}
