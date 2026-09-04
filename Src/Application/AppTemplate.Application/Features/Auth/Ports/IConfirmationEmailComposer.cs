using AppTemplate.Application.Common.Abstractions;

namespace AppTemplate.Application.Features.Auth.Ports;

/// <summary>
/// Renders the confirmation message. It does not deliver it: an implementation owns the template
/// and the address of the page the link points at, both deployment concerns, while whether to send
/// and what a failed delivery means are the use case's, through <see cref="IEmailSender"/>.
/// </summary>
public interface IConfirmationEmailComposer
{
    /// <param name="userName">User-supplied. An implementation must encode it into the document.</param>
    Task<ConfirmationEmail> ComposeAsync(
        string userName,
        string email,
        string token,
        CancellationToken cancellationToken = default);
}
