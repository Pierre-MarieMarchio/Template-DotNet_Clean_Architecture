namespace AppTemplate.Application.Auth.Features.Auth.Ports.EmailTokens;

/// <param name="RejectionMessage">
/// Describes the submitted address, never the account store, so it is safe to return verbatim. Set
/// only for <see cref="EmailChangeStatus.Rejected"/>.
/// </param>
public sealed record EmailChangeOutcome(EmailChangeStatus Status, string? RejectionMessage = null)
{
    /// <summary>The token was spent and the account answers at the new address.</summary>
    public static EmailChangeOutcome Changed { get; } = new(EmailChangeStatus.Changed);

    /// <summary>Nothing changed, and which of the reasons applied is not disclosed.</summary>
    public static EmailChangeOutcome InvalidToken { get; } = new(EmailChangeStatus.InvalidToken);

    /// <summary>The token held; the store would not take the address itself.</summary>
    /// <param name="message">Describes only what was submitted, so it may travel back verbatim.</param>
    public static EmailChangeOutcome Rejected(string message) => new(EmailChangeStatus.Rejected, message);
}
