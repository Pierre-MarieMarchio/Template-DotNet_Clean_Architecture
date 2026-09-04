namespace AppTemplate.Application.Features.Auth.Ports.EmailChangeTokens;

/// <param name="RejectionMessage">
/// Describes the submitted address, never the account store, so it is safe to return verbatim. Set
/// only for <see cref="EmailChangeConfirmationOutcome.Rejected"/>.
/// </param>
public sealed record EmailChangeConfirmation(EmailChangeConfirmationOutcome Outcome, string? RejectionMessage = null)
{
    public static EmailChangeConfirmation Changed { get; } = new(EmailChangeConfirmationOutcome.Changed);

    public static EmailChangeConfirmation NoSuchAccount { get; } = new(EmailChangeConfirmationOutcome.NoSuchAccount);

    public static EmailChangeConfirmation InvalidToken { get; } = new(EmailChangeConfirmationOutcome.InvalidToken);

    public static EmailChangeConfirmation Rejected(string message) =>
        new(EmailChangeConfirmationOutcome.Rejected, message);
}
