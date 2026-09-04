namespace AppTemplate.Application.Features.Auth.Ports.EmailChangeEmailFactory;

/// <summary>
/// Renders the email-change confirmation message. It does not deliver it: an implementation owns
/// the template and the address of the page the link points at, both deployment concerns, while
/// whether to send and what a failed delivery means are the use case's, through
/// <c>IEmailSender</c>.
/// </summary>
public interface IEmailChangeEmailFactory
{
    /// <param name="userName">User-supplied. An implementation must encode it into the document.</param>
    /// <param name="newEmail">The address being moved to, and where the message is delivered.</param>
    Task<EmailChangeEmail> CreateAsync(
        string userName,
        string newEmail,
        string token,
        CancellationToken cancellationToken = default);
}
