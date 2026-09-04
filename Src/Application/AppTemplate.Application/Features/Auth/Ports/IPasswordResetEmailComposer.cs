using AppTemplate.Application.Common.Abstractions;

namespace AppTemplate.Application.Features.Auth.Ports;

/// <summary>
/// Renders the password-reset message. It does not deliver it, for the reason given on
/// <see cref="IConfirmationEmailComposer"/>.
/// </summary>
public interface IPasswordResetEmailComposer
{
    /// <param name="userName">User-supplied. An implementation must encode it into the document.</param>
    Task<PasswordResetEmail> ComposeAsync(
        string userName,
        string email,
        string token,
        CancellationToken cancellationToken = default);
}
