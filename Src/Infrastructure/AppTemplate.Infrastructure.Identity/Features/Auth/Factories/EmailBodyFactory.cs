using System.Globalization;
using System.Net;

namespace AppTemplate.Infrastructure.Identity.Features.Auth.Factories;

/// <summary>
/// One embedded HTML template, and the body it renders. The three auth mails this module sends
/// differ only in which template they name, which placeholders they fill and where their link
/// points; everything between those — reading the resource, encoding, substituting — is this.
/// </summary>
/// <remarks>
/// Every substituted value is HTML-encoded: a user could otherwise put markup — including an anchor
/// pointing anywhere — into their own username and have it delivered inside a mail from this domain.
/// <para>
/// Each factory holds its own instance in a <c>static readonly</c> field, so a template is read once
/// for the process and by exactly one thread. The factories themselves are scoped, so an
/// unsynchronised static string would be process-wide state written from every request that sends
/// one of these mails.
/// </para>
/// </remarks>
/// <param name="templateResourceSuffix">
/// The tail of the embedded resource's name, which is what identifies it: the full name carries the
/// assembly and folder in front, and a module renamed anywhere above the template would break a
/// match written out in full.
/// </param>
internal sealed class EmailBodyFactory(string templateResourceSuffix)
{
    private readonly Lazy<Task<string>> _template = new(() => ReadTemplateAsync(templateResourceSuffix));

    /// <summary>
    /// The link one of these mails carries: parameters URL-encoded, in the fragment rather than the
    /// query, so the single-use token never reaches a server log, a browser history entry or a
    /// <c>Referer</c> header.
    /// </summary>
    public static string LinkTo(Uri page, string email, string token) =>
        string.Format(
            CultureInfo.InvariantCulture,
            "{0}#email={1}&token={2}",
            page.AbsoluteUri,
            Uri.EscapeDataString(email),
            Uri.EscapeDataString(token));

    public async Task<string> CreateAsync(IReadOnlyDictionary<string, string> placeholders)
    {
        string body = await _template.Value;

        foreach (var placeholder in placeholders)
        {
            body = body.Replace(
                $"{{{{{placeholder.Key}}}}}",
                WebUtility.HtmlEncode(placeholder.Value),
                StringComparison.Ordinal);
        }

        return body;
    }

    /// <summary>
    /// The template is an embedded resource rather than a copied content file, so a deployment
    /// cannot lose it and turn every one of these mails into a <c>FileNotFoundException</c>.
    /// </summary>
    private static async Task<string> ReadTemplateAsync(string resourceSuffix)
    {
        var assembly = typeof(EmailBodyFactory).Assembly;
        string resourceName = Array.Find(
                assembly.GetManifestResourceNames(),
                name => name.EndsWith(resourceSuffix, StringComparison.Ordinal))
            ?? throw new InvalidOperationException(
                $"The embedded email template '{resourceSuffix}' was not found in {assembly.GetName().Name}.");

        await using var stream = assembly.GetManifestResourceStream(resourceName)
            ?? throw new InvalidOperationException($"The embedded email template '{resourceName}' could not be opened.");

        using var reader = new StreamReader(stream);

        return await reader.ReadToEndAsync();
    }
}
