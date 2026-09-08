using AppTemplate.Application.Auth.Features.Auth.Ports.PasswordResetEmailFactory;
using AppTemplate.Application.Core.Common.Localization;
using AppTemplate.Infrastructure.Auth.Features.Auth.Options;
using AppTemplate.Infrastructure.Core.Common.Templating;
using Microsoft.Extensions.Options;

namespace AppTemplate.Infrastructure.Auth.Features.Auth.Factories;

/// <summary>
/// Renders the password-reset email and hands it back for the caller to deliver. What it names is
/// all that distinguishes it: its template, its placeholders and the page its link points at — see
/// <see cref="EmailTemplate"/> for the encoding those go through.
/// </summary>
internal sealed class PasswordResetEmailFactory(IOptions<PasswordResetOptions> options)
    : IPasswordResetEmailFactory
{
    private static readonly EmailTemplate _template = new(
        typeof(PasswordResetEmailFactory).Assembly,
        "PasswordResetEmailTemplate");

    public Task<PasswordResetEmail> CreateAsync(
        string userName,
        string email,
        string token,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var settings = options.Value;

        // Guaranteed non-null by PasswordResetOptionsValidator at startup.
        var resetPasswordUrl = settings.ResetPasswordUrl
            ?? throw new InvalidOperationException(
                $"'{PasswordResetOptions.SectionName}:ResetPasswordUrl' is not configured.");

        var rendered = _template.Render(
            CurrentLanguage.Current,
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["UserName"] = userName,
                ["ResetLink"] = EmailLinkFactory.Create(resetPasswordUrl, email, token),
            });

        return Task.FromResult(new PasswordResetEmail(rendered.Subject, rendered.Body));
    }
}
