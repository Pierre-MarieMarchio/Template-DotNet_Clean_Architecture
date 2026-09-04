using System.Globalization;
using System.Net;
using AppTemplate.Application.Features.Auth.Ports.PasswordResetEmailFactory;
using AppTemplate.Infrastructure.Identity.EmailConfirmation;
using Microsoft.Extensions.Options;

namespace AppTemplate.Infrastructure.Identity.PasswordReset;

/// <summary>
/// Renders the password-reset email and hands it back for the caller to deliver. Encoding and link
/// construction mirror <see cref="ConfirmationEmailFactory"/> exactly — see there for why every
/// substituted value is HTML-encoded and why the token travels in the link's fragment.
/// </summary>
internal sealed class PasswordResetEmailFactory(IOptions<PasswordResetOptions> options)
    : IPasswordResetEmailFactory
{
    private const string _templateResourceSuffix = "PasswordReset.PasswordResetEmailTemplate.html";

    /// <summary>Read once for the process, and by exactly one thread. See <see cref="ConfirmationEmailFactory"/>.</summary>
    private static readonly Lazy<Task<string>> _template = new(ReadTemplateAsync);

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

        string resetLink = BuildResetLink(resetPasswordUrl, email, token);

        string body = await RenderAsync(
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["UserName"] = userName,
                ["ResetLink"] = resetLink,
            });

        return new PasswordResetEmail(settings.Subject, body);
    }

    private static string BuildResetLink(Uri resetPasswordUrl, string email, string token) =>
        string.Format(
            CultureInfo.InvariantCulture,
            "{0}#email={1}&token={2}",
            resetPasswordUrl.AbsoluteUri,
            Uri.EscapeDataString(email),
            Uri.EscapeDataString(token));

    private static async Task<string> RenderAsync(IReadOnlyDictionary<string, string> placeholders)
    {
        string template = await _template.Value;

        foreach (var placeholder in placeholders)
        {
            template = template.Replace(
                $"{{{{{placeholder.Key}}}}}",
                WebUtility.HtmlEncode(placeholder.Value),
                StringComparison.Ordinal);
        }

        return template;
    }

    /// <summary>Embedded rather than copied, for the reason <see cref="ConfirmationEmailFactory"/> gives.</summary>
    private static async Task<string> ReadTemplateAsync()
    {
        var assembly = typeof(PasswordResetEmailFactory).Assembly;
        string resourceName = Array.Find(
                assembly.GetManifestResourceNames(),
                name => name.EndsWith(_templateResourceSuffix, StringComparison.Ordinal))
            ?? throw new InvalidOperationException(
                $"The embedded email template '{_templateResourceSuffix}' was not found in {assembly.GetName().Name}.");

        await using var stream = assembly.GetManifestResourceStream(resourceName)
            ?? throw new InvalidOperationException($"The embedded email template '{resourceName}' could not be opened.");

        using var reader = new StreamReader(stream);

        return await reader.ReadToEndAsync();
    }
}
