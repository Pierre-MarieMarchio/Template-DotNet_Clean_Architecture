namespace AppTemplate.Application.Features.Auth.Ports.EmailChangeTokens;

/// <param name="RejectionMessage">
/// Describes the submitted address, never the account store, so it is safe to return verbatim. Set
/// only for <see cref="EmailChangeConfirmationStatus.Rejected"/>.
/// </param>
public sealed record EmailChangeConfirmationOutcome(EmailChangeConfirmationStatus Status, string? RejectionMessage = null)
{
    public static EmailChangeConfirmationOutcome Changed { get; } = new(EmailChangeConfirmationStatus.Changed);

    public static EmailChangeConfirmationOutcome NoSuchAccount { get; } = new(EmailChangeConfirmationStatus.NoSuchAccount);

    public static EmailChangeConfirmationOutcome InvalidToken { get; } = new(EmailChangeConfirmationStatus.InvalidToken);

    public static EmailChangeConfirmationOutcome Rejected(string message) =>
        new(EmailChangeConfirmationStatus.Rejected, message);
}
