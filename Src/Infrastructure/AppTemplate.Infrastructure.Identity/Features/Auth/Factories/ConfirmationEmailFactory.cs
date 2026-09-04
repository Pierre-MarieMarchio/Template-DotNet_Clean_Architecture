using AppTemplate.Application.Features.Auth.Ports.ConfirmationEmailFactory;
using AppTemplate.Infrastructure.Identity.Features.Auth.Options;
using Microsoft.Extensions.Options;

namespace AppTemplate.Infrastructure.Identity.Features.Auth.Factories;

/// <summary>
/// Renders the confirmation email and hands it back for the caller to deliver. What it names is all
/// that distinguishes it: its template, its placeholders and the page its link points at — see
/// <see cref="EmailBodyFactory"/> for the encoding those three go through.
/// </summary>
internal sealed class ConfirmationEmailFactory(IOptions<EmailConfirmationOptions> options)
    : IConfirmationEmailFactory
{
    private static readonly EmailBodyFactory _body = new("Templates.RegisterEmailTemplate.html");

    public async Task<ConfirmationEmail> CreateAsync(
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

        string body = await _body.CreateAsync(
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["UserName"] = userName,
                ["ConfirmationLink"] = EmailBodyFactory.LinkTo(confirmEmailUrl, email, token),
            });

        return new ConfirmationEmail(settings.Subject, body);
    }
}
