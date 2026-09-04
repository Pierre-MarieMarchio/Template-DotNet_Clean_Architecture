namespace AppTemplate.Application.Features.Auth.Ports;

/// <param name="RejectionMessage">
/// Describes the submitted password, never the account store, so it is safe to return verbatim. Set
/// only for <see cref="PasswordChangeOutcome.Rejected"/>.
/// </param>
public sealed record PasswordChange(PasswordChangeOutcome Outcome, string? RejectionMessage = null)
{
    public static PasswordChange Changed { get; } = new(PasswordChangeOutcome.Changed);

    public static PasswordChange IncorrectCurrentPassword { get; } =
        new(PasswordChangeOutcome.IncorrectCurrentPassword);

    public static PasswordChange Rejected(string message) => new(PasswordChangeOutcome.Rejected, message);
}
