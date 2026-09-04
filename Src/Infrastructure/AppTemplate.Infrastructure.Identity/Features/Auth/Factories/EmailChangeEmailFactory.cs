using AppTemplate.Application.Features.Auth.Ports.EmailChangeEmailFactory;
using AppTemplate.Infrastructure.Identity.Features.Auth.Options;
using Microsoft.Extensions.Options;

namespace AppTemplate.Infrastructure.Identity.Features.Auth.Factories;

/// <summary>
/// Renders the email-change confirmation message and hands it back for the caller to deliver. What
/// it names is all that distinguishes it: its template, its placeholders and the page its link
/// points at — see <see cref="EmailBodyFactory"/> for the encoding those three go through.
/// </summary>
internal sealed class EmailChangeEmailFactory(IOptions<EmailChangeOptions> options)
    : IEmailChangeEmailFactory
{
    private static readonly EmailBodyFactory _body = new("EmailChangeEmailTemplate");

    public async Task<EmailChangeEmail> CreateAsync(
        string userName,
        string newEmail,
        string token,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var settings = options.Value;

        // Guaranteed non-null by EmailChangeOptionsValidator at startup.
        var confirmEmailChangeUrl = settings.ConfirmEmailChangeUrl
            ?? throw new InvalidOperationException(
                $"'{EmailChangeOptions.SectionName}:ConfirmEmailChangeUrl' is not configured.");

        var rendered = await _body.CreateAsync(
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["UserName"] = userName,
                ["ConfirmationLink"] = EmailBodyFactory.LinkTo(confirmEmailChangeUrl, newEmail, token),
            });

        return new EmailChangeEmail(rendered.Subject, rendered.Body);
    }
}
