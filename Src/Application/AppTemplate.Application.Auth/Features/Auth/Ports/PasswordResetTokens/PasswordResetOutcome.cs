namespace AppTemplate.Application.Auth.Features.Auth.Ports.PasswordResetTokens;

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
    /// <summary>The token is spent and the new password is in place.</summary>
    public static PasswordResetOutcome Succeeded(Guid userId) => new(PasswordResetStatus.Reset, userId, null);

    /// <summary>
    /// The address names no account. Reaches the caller as the same error <see cref="InvalidToken"/> does.
    /// </summary>
    public static PasswordResetOutcome NoSuchAccount { get; } = new(PasswordResetStatus.NoSuchAccount, null, null);

    /// <summary>The token would not redeem, and which of the reasons applied is not disclosed.</summary>
    public static PasswordResetOutcome InvalidToken { get; } = new(PasswordResetStatus.InvalidToken, null, null);

    /// <summary>The token held; the store refused the new password itself.</summary>
    public static PasswordResetOutcome Rejected(string message) => new(PasswordResetStatus.Rejected, null, message);
}
