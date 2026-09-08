namespace AppTemplate.Application.Auth.Features.Auth.Ports.EmailChangeTokens;

/// <param name="RejectionMessage">
/// Describes the submitted address, never the account store, so it is safe to return verbatim. Set
/// only for <see cref="EmailChangeConfirmationStatus.Rejected"/>.
/// </param>
public sealed record EmailChangeConfirmationOutcome(EmailChangeConfirmationStatus Status, string? RejectionMessage = null)
{
    /// <summary>The token was spent and the account answers at the new address.</summary>
    public static EmailChangeConfirmationOutcome Changed { get; } = new(EmailChangeConfirmationStatus.Changed);

    /// <summary>The id the token was issued for no longer names an account.</summary>
    public static EmailChangeConfirmationOutcome NoSuchAccount { get; } = new(EmailChangeConfirmationStatus.NoSuchAccount);

    /// <summary>Nothing changed, and which of the four reasons applied is not disclosed.</summary>
    public static EmailChangeConfirmationOutcome InvalidToken { get; } = new(EmailChangeConfirmationStatus.InvalidToken);

    /// <summary>The token held; the store would not take the address itself.</summary>
    /// <param name="message">Describes only what was submitted, so it may travel back verbatim.</param>
    public static EmailChangeConfirmationOutcome Rejected(string message) =>
        new(EmailChangeConfirmationStatus.Rejected, message);
}
