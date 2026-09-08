using AppTemplate.Application.Auth.Features.Auth.Ports.ConfirmationEmailFactory;
using AppTemplate.Application.Core.Common.Localization;
using AppTemplate.Infrastructure.Core.Common.Templating;
using AppTemplate.Infrastructure.Identity.Features.Auth.Options;
using Microsoft.Extensions.Options;

namespace AppTemplate.Infrastructure.Identity.Features.Auth.Factories;

/// <summary>
/// Renders the confirmation email and hands it back for the caller to deliver. What it names is all
/// that distinguishes it: its template, its placeholders and the page its link points at — see
/// <see cref="EmailTemplate"/> for the encoding those go through.
/// </summary>
internal sealed class ConfirmationEmailFactory(IOptions<EmailConfirmationOptions> options)
    : IConfirmationEmailFactory
{
    private static readonly EmailTemplate _template = new(
        typeof(ConfirmationEmailFactory).Assembly,
        "RegisterEmailTemplate");

    public Task<ConfirmationEmail> CreateAsync(
        string userName,
        string email,
        string token,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var settings = options.Value;

        // Guaranteed non-null by EmailConfirmationOptionsValidator at startup.
        var confirmEmailUrl = settings.ConfirmEmailUrl
            ?? throw new InvalidOperationException(
                $"'{EmailConfirmationOptions.SectionName}:ConfirmEmailUrl' is not configured.");

        var rendered = _template.Render(
            CurrentLanguage.Current,
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["UserName"] = userName,
                ["ConfirmationLink"] = EmailLinkFactory.Create(confirmEmailUrl, email, token),
            });

        return Task.FromResult(new ConfirmationEmail(rendered.Subject, rendered.Body));
    }
}
