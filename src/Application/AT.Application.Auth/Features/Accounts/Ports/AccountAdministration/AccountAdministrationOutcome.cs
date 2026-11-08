namespace AppTemplate.Application.Auth.Features.Accounts.Ports.AccountAdministration;

public sealed record AccountAdministrationOutcome(AccountAdministrationStatus Status, string? RejectionMessage = null)
{
    public static AccountAdministrationOutcome Applied { get; } = new(AccountAdministrationStatus.Applied);

    public static AccountAdministrationOutcome NoSuchAccount { get; } = new(AccountAdministrationStatus.NoSuchAccount);

    public static AccountAdministrationOutcome Rejected { get; } = new(AccountAdministrationStatus.Rejected);

    public static AccountAdministrationOutcome RejectedBecause(string message) =>
        new(AccountAdministrationStatus.Rejected, message);
}
