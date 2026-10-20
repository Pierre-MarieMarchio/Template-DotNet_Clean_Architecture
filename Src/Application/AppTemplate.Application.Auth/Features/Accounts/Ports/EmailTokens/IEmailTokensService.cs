namespace AppTemplate.Application.Auth.Features.Accounts.Ports.EmailTokens;

public interface IEmailTokensService
{
    Task<string?> IssueEmailConfirmationAsync(string email, CancellationToken cancellationToken = default);

    Task<bool> ConfirmEmailAsync(string email, string token, CancellationToken cancellationToken = default);

    Task<string?> IssuePasswordResetAsync(string email, CancellationToken cancellationToken = default);

    Task<PasswordResetOutcome> ResetPasswordAsync(
        string email,
        string token,
        string newPassword,
        CancellationToken cancellationToken = default);

    Task<EmailChangeRequestOutcome> IssueEmailChangeAsync(
        Guid userId,
        string currentPassword,
        string newEmail,
        CancellationToken cancellationToken = default);

    Task<EmailChangeOutcome> ChangeEmailAsync(
        Guid userId,
        string newEmail,
        string token,
        CancellationToken cancellationToken = default);
}
