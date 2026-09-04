using AppTemplate.Application.Features.Auth.Ports.PasswordResetEmailFactory;
using AppTemplate.Infrastructure.Identity.Features.Auth.Options;
using Microsoft.Extensions.Options;

namespace AppTemplate.Infrastructure.Identity.Features.Auth.Factories;

/// <summary>
/// Renders the password-reset email and hands it back for the caller to deliver. What it names is
/// all that distinguishes it: its template, its placeholders and the page its link points at — see
/// <see cref="EmailBodyFactory"/> for the encoding those three go through.
/// </summary>
internal sealed class PasswordResetEmailFactory(IOptions<PasswordResetOptions> options)
    : IPasswordResetEmailFactory
{
    private static readonly EmailBodyFactory _body = new("Templates.PasswordResetEmailTemplate.html");

    public async Task<PasswordResetEmail> CreateAsync(
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

        string body = await _body.CreateAsync(
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["UserName"] = userName,
                ["ResetLink"] = EmailBodyFactory.LinkTo(resetPasswordUrl, email, token),
            });

        return new PasswordResetEmail(settings.Subject, body);
    }
}
