using System.Globalization;
using System.Net;
using AppTemplate.Application.Features.Auth.Ports.EmailChangeEmailFactory;
using AppTemplate.Infrastructure.Identity.Features.Auth.Options;
using Microsoft.Extensions.Options;

namespace AppTemplate.Infrastructure.Identity.Features.Auth.Factories;

/// <summary>
/// Renders the email-change confirmation message and hands it back for the caller to deliver.
/// Encoding and link construction mirror <see cref="ConfirmationEmailFactory"/> exactly — see there
/// for why every substituted value is HTML-encoded and why the token travels in the link's fragment.
/// </summary>
internal sealed class EmailChangeEmailFactory(IOptions<EmailChangeOptions> options)
    : IEmailChangeEmailFactory
{
    private const string _templateResourceSuffix = "EmailChange.EmailChangeEmailTemplate.html";

    /// <summary>Read once for the process, and by exactly one thread. See <see cref="ConfirmationEmailFactory"/>.</summary>
    private static readonly Lazy<Task<string>> _template = new(ReadTemplateAsync);

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

        string confirmationLink = BuildConfirmationLink(confirmEmailChangeUrl, newEmail, token);

        string body = await RenderAsync(
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["UserName"] = userName,
                ["ConfirmationLink"] = confirmationLink,
            });

        return new EmailChangeEmail(settings.Subject, body);
    }

    private static string BuildConfirmationLink(Uri confirmEmailChangeUrl, string newEmail, string token) =>
        string.Format(
            CultureInfo.InvariantCulture,
            "{0}#email={1}&token={2}",
            confirmEmailChangeUrl.AbsoluteUri,
            Uri.EscapeDataString(newEmail),
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
        var assembly = typeof(EmailChangeEmailFactory).Assembly;
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
