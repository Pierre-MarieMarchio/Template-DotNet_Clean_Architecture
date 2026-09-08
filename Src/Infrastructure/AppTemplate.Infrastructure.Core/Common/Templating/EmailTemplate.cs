using System.Net;
using System.Reflection;
using System.Text.RegularExpressions;
using AppTemplate.Application.Core.Common.Localization;

namespace AppTemplate.Infrastructure.Core.Common.Templating;

/// <summary>
/// One mail's embedded templates — one per language — and the body they render.
/// </summary>
/// <remarks>
/// <para>
/// A template is an embedded resource named <c>&lt;baseName&gt;.&lt;culture&gt;.html</c>, and the
/// cultures a mail is available in are the ones with a resource embedded: shipping a language is
/// adding a file. The resource must be declared with <c>WithCulture="false"</c>, or MSBuild reads
/// the culture in the file name and compiles the template into a satellite assembly, where this
/// class cannot see it.
/// </para>
/// <para>
/// The subject is the template's <c>&lt;title&gt;</c>, not a separate setting: a subject and a body
/// described in two places can be delivered in two different languages, and nothing about either
/// half is wrong on its own. Placeholders are written <c>{{Name}}</c> and every substituted value is
/// HTML-encoded, so a value a user chose — their own name — cannot carry markup into a mail sent
/// from this domain. A subject takes no placeholder: a mail header is not HTML, so the body's
/// encoding would be the wrong one, and an unencoded newline in a header is header injection.
/// </para>
/// <para>
/// Resources are read once, on the first render, and the instance is safe to share between threads.
/// </para>
/// </remarks>
public sealed class EmailTemplate
{
    private static readonly Regex _title = new(
        @"<title>(?<subject>.*?)</title>",
        RegexOptions.Singleline | RegexOptions.IgnoreCase,
        TimeSpan.FromSeconds(5));

    private readonly Assembly _resources;
    private readonly string _baseName;
    private readonly Lazy<Dictionary<string, RenderedEmail>> _templates;

    /// <param name="resources">
    /// The assembly holding the templates. Passed in rather than read from this class, so a module
    /// renders its own mail out of its own resources.
    /// </param>
    /// <param name="baseName">
    /// The template family's file-name stem — <c>RegisterEmailTemplate</c> — which the culture and
    /// the extension follow. Only the stem, because the full resource name carries the assembly and
    /// the folder in front, and a module or folder renamed above the templates would break a name
    /// written out in full.
    /// </param>
    public EmailTemplate(Assembly resources, string baseName)
    {
        ArgumentNullException.ThrowIfNull(resources);
        ArgumentException.ThrowIfNullOrWhiteSpace(baseName);

        _resources = resources;
        _baseName = baseName;
        _templates = new Lazy<Dictionary<string, RenderedEmail>>(Read);
    }

    /// <summary>The cultures this mail has a template for, which is what makes them available.</summary>
    public IReadOnlyCollection<string> AvailableCultures => _templates.Value.Keys;

    /// <summary>
    /// Renders in the closest available language to <paramref name="languageTag"/>: itself, then
    /// each broader tag in turn — so <c>fr-CA</c> reaches the <c>fr</c> template — and
    /// <see cref="CurrentLanguage.FallbackTag"/> when none of them ships one.
    /// </summary>
    /// <param name="languageTag">The reader's language, usually <see cref="CurrentLanguage.Current"/>.</param>
    /// <param name="placeholders">Values for the <c>{{Name}}</c> placeholders, HTML-encoded on the way in.</param>
    /// <exception cref="InvalidOperationException">
    /// No template is embedded for the fallback culture, so a reader whose language is unavailable
    /// has nothing to receive.
    /// </exception>
    public RenderedEmail Render(string languageTag, IReadOnlyDictionary<string, string> placeholders)
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

        return templates.TryGetValue(CurrentLanguage.FallbackTag, out var fallback)
            ? fallback
            : throw new InvalidOperationException(
                $"'{_baseName}' has no template for the fallback culture "
                + $"'{CurrentLanguage.FallbackTag}'. Every mail must be writable in it, because it "
                + "is what an unmatched reader receives.");
    }

    private Dictionary<string, RenderedEmail> Read()
    {
        var found = new Dictionary<string, RenderedEmail>(StringComparer.OrdinalIgnoreCase);
        string prefix = $".{_baseName}.";

        foreach (string resource in _resources.GetManifestResourceNames())
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

            found[culture] = Parse(resource);
        }

        return found.Count > 0
            ? found
            : throw new InvalidOperationException(
                $"No embedded template matched '{_baseName}.<culture>.html' in "
                + $"{_resources.GetName().Name}.");
    }

    private RenderedEmail Parse(string resourceName)
    {
        using var reader = new StreamReader(
            _resources.GetManifestResourceStream(resourceName)
            ?? throw new InvalidOperationException(
                $"The embedded email template '{resourceName}' could not be opened."));

        string text = reader.ReadToEnd();
        var title = _title.Match(text);

        if (!title.Success || string.IsNullOrWhiteSpace(title.Groups["subject"].Value))
        {
            throw new InvalidOperationException(
                $"The embedded email template '{resourceName}' has no non-empty <title>, which is "
                + "the mail's subject. A mail with no subject is not one this template will send.");
        }

        return new RenderedEmail(
            WebUtility.HtmlDecode(title.Groups["subject"].Value).Trim(),
            text);
    }
}
