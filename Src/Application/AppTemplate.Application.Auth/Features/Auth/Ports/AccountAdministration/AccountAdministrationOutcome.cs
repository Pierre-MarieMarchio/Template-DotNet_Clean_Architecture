namespace AppTemplate.Application.Auth.Features.Auth.Ports.AccountAdministration;

/// <param name="RejectionMessage">
/// Names the role or describes why the store refused, so it is safe to return verbatim. Set only when
/// the store said why; for <see cref="AccountAdministrationStatus.Rejected"/> alone.
/// </param>
public sealed record AccountAdministrationOutcome(AccountAdministrationStatus Status, string? RejectionMessage = null)
{
    /// <summary>The account now matches what was asked.</summary>
    public static AccountAdministrationOutcome Applied { get; } = new(AccountAdministrationStatus.Applied);

    /// <summary>No account has that id, so nothing changed.</summary>
    public static AccountAdministrationOutcome NoSuchAccount { get; } = new(AccountAdministrationStatus.NoSuchAccount);

    /// <summary>The account exists; the store refused the change and gave no reason worth showing.</summary>
    public static AccountAdministrationOutcome Rejected { get; } = new(AccountAdministrationStatus.Rejected);

    /// <summary>The account exists; the store refused the change for the reason given.</summary>
    public static AccountAdministrationOutcome RejectedBecause(string message) =>
        new(AccountAdministrationStatus.Rejected, message);
}
