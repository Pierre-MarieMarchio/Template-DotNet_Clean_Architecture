using System.Globalization;
using System.Net;
using AppTemplate.Application.Features.Auth.Ports.ConfirmationEmailFactory;
using AppTemplate.Infrastructure.Identity.Features.Auth.Options;
using Microsoft.Extensions.Options;

namespace AppTemplate.Infrastructure.Identity.Features.Auth.Factories;

/// <summary>
/// Renders the confirmation email and hands it back for the caller to deliver.
///
/// Every substituted value is HTML-encoded: a user could otherwise put markup — including an anchor
/// pointing anywhere — into their own username and have it delivered inside a mail from this domain.
/// The confirmation parameters are URL-encoded and travel in the link's fragment, so the single-use
/// token never reaches a server log, a browser history entry or a <c>Referer</c> header.
/// </summary>
internal sealed class ConfirmationEmailFactory(IOptions<EmailConfirmationOptions> options)
    : IConfirmationEmailFactory
{
    private const string _templateResourceSuffix = "EmailConfirmation.RegisterEmailTemplate.html";

    /// <summary>
    /// Read once for the process, and by exactly one thread. This service is scoped, so a plain static
    /// field would be process-wide state written without synchronisation from every request that
    /// registers an account.
    /// </summary>
    private static readonly Lazy<Task<string>> _template = new(ReadTemplateAsync);

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

        string confirmationLink = BuildConfirmationLink(confirmEmailUrl, email, token);

        string body = await RenderAsync(
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["UserName"] = userName,
                ["ConfirmationLink"] = confirmationLink,
            });

        return new ConfirmationEmail(settings.Subject, body);
    }

    private static string BuildConfirmationLink(Uri confirmEmailUrl, string email, string token) =>
        string.Format(
            CultureInfo.InvariantCulture,
            "{0}#email={1}&token={2}",
            confirmEmailUrl.AbsoluteUri,
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

    /// <summary>
    /// The template is an embedded resource rather than a copied content file, so a deployment
    /// cannot lose it and turn every registration into a <c>FileNotFoundException</c>.
    /// </summary>
    private static async Task<string> ReadTemplateAsync()
    {
        var assembly = typeof(ConfirmationEmailFactory).Assembly;
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
