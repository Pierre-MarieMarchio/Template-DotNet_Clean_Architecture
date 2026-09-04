namespace AppTemplate.Application.Features.Auth.Ports;

/// <param name="UserId">
/// Set only on <see cref="PasswordResetOutcome.Reset"/>, so the caller can revoke that account's
/// refresh tokens without a second lookup.
/// </param>
/// <param name="RejectionMessage">
/// Describes the submitted password, never the account store, so it is safe to return verbatim. Set
/// only for <see cref="PasswordResetOutcome.Rejected"/>.
/// </param>
public sealed record PasswordReset(PasswordResetOutcome Outcome, Guid? UserId, string? RejectionMessage)
{
    public static PasswordReset Succeeded(Guid userId) => new(PasswordResetOutcome.Reset, userId, null);

    public static PasswordReset NoSuchAccount { get; } = new(PasswordResetOutcome.NoSuchAccount, null, null);

    public static PasswordReset InvalidToken { get; } = new(PasswordResetOutcome.InvalidToken, null, null);

    public static PasswordReset Rejected(string message) => new(PasswordResetOutcome.Rejected, null, message);
}
