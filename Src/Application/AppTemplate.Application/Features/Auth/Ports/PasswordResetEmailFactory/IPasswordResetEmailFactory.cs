using AppTemplate.Application.Features.Auth.Ports.ConfirmationEmailFactory;

namespace AppTemplate.Application.Features.Auth.Ports.PasswordResetEmailFactory;

/// <summary>
/// Renders the password-reset message. It does not deliver it, for the reason given on
/// <see cref="IConfirmationEmailFactory"/>.
/// </summary>
public interface IPasswordResetEmailFactory
{
    /// <param name="userName">User-supplied. An implementation must encode it into the document.</param>
    Task<PasswordResetEmail> CreateAsync(
        string userName,
        string email,
        string token,
        CancellationToken cancellationToken = default);
}
