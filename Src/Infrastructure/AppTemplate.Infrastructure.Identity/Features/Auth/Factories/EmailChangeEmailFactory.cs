using AppTemplate.Application.Auth.Features.Auth.Ports.EmailChangeEmailFactory;
using AppTemplate.Application.Core.Common.Localization;
using AppTemplate.Infrastructure.Core.Common.Templating;
using AppTemplate.Infrastructure.Identity.Features.Auth.Options;
using Microsoft.Extensions.Options;

namespace AppTemplate.Infrastructure.Identity.Features.Auth.Factories;

/// <summary>
/// Renders the email-change confirmation message and hands it back for the caller to deliver. What
/// it names is all that distinguishes it: its template, its placeholders and the page its link
/// points at — see <see cref="EmailTemplate"/> for the encoding those go through.
/// </summary>
internal sealed class EmailChangeEmailFactory(IOptions<EmailChangeOptions> options)
    : IEmailChangeEmailFactory
{
    private static readonly EmailTemplate _template = new(
        typeof(EmailChangeEmailFactory).Assembly,
        "EmailChangeEmailTemplate");

    public Task<EmailChangeEmail> CreateAsync(
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

        var rendered = _template.Render(
            CurrentLanguage.Current,
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["UserName"] = userName,
                ["ConfirmationLink"] = EmailLinkFactory.Create(confirmEmailChangeUrl, newEmail, token),
            });

        return Task.FromResult(new EmailChangeEmail(rendered.Subject, rendered.Body));
    }
}
