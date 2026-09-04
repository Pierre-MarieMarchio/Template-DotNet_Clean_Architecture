using System.Globalization;
using System.Net;
using System.Text.RegularExpressions;
using AppTemplate.Application.Common.Localization;

namespace AppTemplate.Infrastructure.Identity.Features.Auth.Factories;

/// <summary>One rendered mail: the subject and the body agree about their language by construction.</summary>
internal sealed record RenderedEmail(string Subject, string Body);

/// <summary>
/// One mail's embedded templates — one per language — and the body they render. The three auth mails
/// this module sends differ only in which template family they name, which placeholders they fill
/// and where their link points; everything between those is this.
/// </summary>
/// <remarks>
/// <para>
/// <b>The subject comes from the template's <c>&lt;title&gt;</c>, not from configuration.</b> A
/// subject in <c>appsettings</c> and a body in a template are one mail described in two places, in
/// two languages as soon as more than one ships — which is exactly what went wrong here before: a
/// French body was delivered under an English subject for months, and no gate could see it because
/// neither half was wrong on its own. There is no placeholder substitution in a subject: a mail
/// header is not HTML, so the body's encoding would be the wrong one, and an unencoded value with a
/// newline in it is header injection.
/// </para>
/// <para>
/// <b>Which languages exist is discovered, not declared.</b> The available cultures are the ones
/// with a template embedded in this assembly, so shipping a language is adding two files and
/// nothing else. A list in configuration would be a second statement of the same fact, free to name
/// a language no template backs.
/// </para>
/// <para>
/// Every substituted value is HTML-encoded: a user could otherwise put markup — including an anchor
/// pointing anywhere — into their own username and have it delivered inside a mail from this domain.
/// </para>
/// <para>
/// Each factory holds its own instance in a <c>static readonly</c> field, so a template family is
/// read once for the process and by exactly one thread. The factories themselves are scoped, so an
/// unsynchronised static string would be process-wide state written from every request that sends
/// one of these mails.
/// </para>
/// </remarks>
/// <param name="templateBaseName">
/// The template family's file-name stem — <c>RegisterEmailTemplate</c> — which the culture and
/// extension follow. Only the stem, because the full resource name carries the assembly and folder
/// in front and a module renamed anywhere above the templates would break a match written out in
/// full.
/// </param>
internal sealed class EmailBodyFactory(string templateBaseName)
{
    /// <summary>
    /// The language a mail falls back to when the reader's own has no template. Preferred over
    /// "whatever sorts first" so that adding a language can never silently change what an
    /// unmatched reader receives.
    /// </summary>
    internal const string FallbackCulture = CurrentLanguage.FallbackTag;

    private static readonly Regex _title = new(
        @"<title>(?<subject>.*?)</title>",
        RegexOptions.Singleline | RegexOptions.IgnoreCase,
        TimeSpan.FromSeconds(5));

    private readonly Lazy<Dictionary<string, RenderedEmail>> _templates =
        new(() => ReadTemplates(templateBaseName));

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

    /// <summary>The cultures this mail has a template for, which is what makes them available.</summary>
    public IReadOnlyCollection<string> AvailableCultures => _templates.Value.Keys;

    /// <summary>
    /// Renders in <see cref="CurrentLanguage.Current"/> — set per request by the API's language
    /// middleware, and once at start-up by the worker, so a mail is written in the language of
    /// whoever it is for rather than of whoever deployed it.
    /// </summary>
    public Task<RenderedEmail> CreateAsync(IReadOnlyDictionary<string, string> placeholders) =>
        Task.FromResult(Create(CurrentLanguage.Current, placeholders));

    internal RenderedEmail Create(string languageTag, IReadOnlyDictionary<string, string> placeholders)
    {
        ArgumentNullException.ThrowIfNull(languageTag);
        ArgumentNullException.ThrowIfNull(placeholders);

        var template = Best(languageTag);
        string body = template.Body;

        foreach (var placeholder in placeholders)
        {
            body = body.Replace(
                $"{{{{{placeholder.Key}}}}}",
                WebUtility.HtmlEncode(placeholder.Value),
                StringComparison.Ordinal);
        }

        return template with { Body = body };
    }

    /// <summary>
    /// The closest template to <paramref name="languageTag"/>: itself, then each broader tag in
    /// turn — so <c>fr-CA</c> reaches the <c>fr</c> template rather than the fallback — and the
    /// fallback when none of them ships one.
    /// </summary>
    private RenderedEmail Best(string languageTag)
    {
        var templates = _templates.Value;

        if (CurrentLanguage.IsWellFormed(languageTag))
        {
            foreach (string candidate in CurrentLanguage.Candidates(languageTag))
            {
                if (templates.TryGetValue(candidate, out var match))
                {
                    return match;
                }
            }
        }

        return templates.TryGetValue(FallbackCulture, out var fallback)
            ? fallback
            : throw new InvalidOperationException(
                $"'{templateBaseName}' has no template for the fallback culture '{FallbackCulture}'. "
                + "Every mail must be writable in it, because it is what an unmatched reader receives.");
    }

    /// <summary>
    /// The templates are embedded resources rather than copied content files, so a deployment cannot
    /// lose one and turn a mail into a <c>FileNotFoundException</c>.
    /// </summary>
    private static Dictionary<string, RenderedEmail> ReadTemplates(string baseName)
    {
        var assembly = typeof(EmailBodyFactory).Assembly;
        var found = new Dictionary<string, RenderedEmail>(StringComparer.OrdinalIgnoreCase);
        string prefix = $".{baseName}.";

        foreach (string resource in assembly.GetManifestResourceNames())
        {
            if (!resource.EndsWith(".html", StringComparison.Ordinal))
            {
                continue;
            }

            int start = resource.IndexOf(prefix, StringComparison.Ordinal);

            if (start < 0)
            {
                continue;
            }

            string culture = resource[(start + prefix.Length)..^".html".Length];

            if (culture.Length == 0 || culture.Contains('.', StringComparison.Ordinal))
            {
                continue;
            }

            found[culture] = Parse(assembly.GetManifestResourceStream(resource), resource);
        }

        return found.Count > 0
            ? found
            : throw new InvalidOperationException(
                $"No embedded template matched '{baseName}.<culture>.html' in {assembly.GetName().Name}.");
    }

    private static RenderedEmail Parse(Stream? stream, string resourceName)
    {
        using var reader = new StreamReader(
            stream ?? throw new InvalidOperationException(
                $"The embedded email template '{resourceName}' could not be opened."));

        string text = reader.ReadToEnd();
        var title = _title.Match(text);

        if (!title.Success || string.IsNullOrWhiteSpace(title.Groups["subject"].Value))
        {
            throw new InvalidOperationException(
                $"The embedded email template '{resourceName}' has no non-empty <title>, which is the "
                + "mail's subject. A mail with no subject is not one this module will send.");
        }

        return new RenderedEmail(
            WebUtility.HtmlDecode(title.Groups["subject"].Value).Trim(),
            text);
    }
}
