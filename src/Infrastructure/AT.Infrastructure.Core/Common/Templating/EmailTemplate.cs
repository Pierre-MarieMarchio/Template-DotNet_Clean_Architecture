using System.Net;
using System.Reflection;
using System.Text.RegularExpressions;
using AppTemplate.Application.Core.Common.Localization;

namespace AppTemplate.Infrastructure.Core.Common.Templating;

public sealed class EmailTemplate
{
    private static readonly Regex _title = new(
        @"<title>(?<subject>.*?)</title>",
        RegexOptions.Singleline | RegexOptions.IgnoreCase,
        TimeSpan.FromSeconds(5));

    private readonly Assembly _resources;
    private readonly string _baseName;
    private readonly Lazy<Dictionary<string, RenderedEmail>> _templates;

    public EmailTemplate(Assembly resources, string baseName)
    {
        ArgumentNullException.ThrowIfNull(resources);
        ArgumentException.ThrowIfNullOrWhiteSpace(baseName);

        _resources = resources;
        _baseName = baseName;
        _templates = new Lazy<Dictionary<string, RenderedEmail>>(Read);
    }

    public IReadOnlyCollection<string> AvailableCultures => _templates.Value.Keys;

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
