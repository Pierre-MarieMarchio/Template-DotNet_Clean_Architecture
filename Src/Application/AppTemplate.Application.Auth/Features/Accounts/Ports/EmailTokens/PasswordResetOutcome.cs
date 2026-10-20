namespace AppTemplate.Application.Auth.Features.Accounts.Ports.EmailTokens;

public sealed record PasswordResetOutcome(PasswordResetStatus Status, Guid? UserId, string? RejectionMessage)
{
    public static PasswordResetOutcome Succeeded(Guid userId) => new(PasswordResetStatus.Reset, userId, null);

    public static PasswordResetOutcome InvalidToken { get; } = new(PasswordResetStatus.InvalidToken, null, null);

    public static PasswordResetOutcome Rejected(string message) => new(PasswordResetStatus.Rejected, null, message);
}
