namespace AppTemplate.Application.Auth.Features.Accounts.Ports.EmailTokens;

public sealed record EmailChangeOutcome(EmailChangeStatus Status, string? RejectionMessage = null)
{
    public static EmailChangeOutcome Changed { get; } = new(EmailChangeStatus.Changed);

    public static EmailChangeOutcome InvalidToken { get; } = new(EmailChangeStatus.InvalidToken);

    public static EmailChangeOutcome Rejected(string message) => new(EmailChangeStatus.Rejected, message);
}
