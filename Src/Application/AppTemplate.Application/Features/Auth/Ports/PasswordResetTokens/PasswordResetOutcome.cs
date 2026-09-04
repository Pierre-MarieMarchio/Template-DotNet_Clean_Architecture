namespace AppTemplate.Application.Features.Auth.Ports.PasswordResetTokens;

/// <param name="UserId">
/// Set only on <see cref="PasswordResetStatus.Reset"/>, so the caller can revoke that account's
/// refresh tokens without a second lookup.
/// </param>
/// <param name="RejectionMessage">
/// Describes the submitted password, never the account store, so it is safe to return verbatim. Set
/// only for <see cref="PasswordResetStatus.Rejected"/>.
/// </param>
public sealed record PasswordResetOutcome(PasswordResetStatus Status, Guid? UserId, string? RejectionMessage)
{
    public static PasswordResetOutcome Succeeded(Guid userId) => new(PasswordResetStatus.Reset, userId, null);

    public static PasswordResetOutcome NoSuchAccount { get; } = new(PasswordResetStatus.NoSuchAccount, null, null);

    public static PasswordResetOutcome InvalidToken { get; } = new(PasswordResetStatus.InvalidToken, null, null);

    public static PasswordResetOutcome Rejected(string message) => new(PasswordResetStatus.Rejected, null, message);
}
