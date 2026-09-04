using System.Net;
using System.Text.RegularExpressions;
using AppTemplate.Application.Common.Localization;

namespace AppTemplate.Infrastructure.Email.Features.Reminders;

/// <summary>One rendered mail: the subject and the body agree about their language by construction.</summary>
internal sealed record RenderedEmail(string Subject, string Body);

/// <summary>
/// The reminder mail's embedded templates, one per language, and the body they render.
/// <para>
/// <b>This is the twin of <c>AppTemplate.Infrastructure.Identity</c>'s <c>EmailBodyFactory</c>, and
/// the duplication is intended.</b> A module may not reference a sibling, so the two cannot share
/// one class; the application layer could hold it, but reading HTML out of an assembly is not a
/// decision that layer should be making. What must not happen is the two drifting apart, which is
/// why <c>EmailTemplateCoverageTests</c> asserts that both modules ship the same languages: a
/// deployment that could write a password reset in French and a reminder only in English is the
/// defect this whole arrangement exists to prevent.
/// </para>
/// <para>
/// The subject is the template's <c>&lt;title&gt;</c>, and the cultures are the ones with a template
/// embedded — both for the reasons the Identity twin gives at length.
/// </para>
/// </summary>
internal sealed class ReminderEmailTemplate
{
    /// <summary>The language a reader with no matching template receives.</summary>
    internal const string FallbackCulture = CurrentLanguage.FallbackTag;

    private const string _baseName = "ReminderEmailTemplate";

    private static readonly Regex _title = new(
        @"<title>(?<subject>.*?)</title>",
        RegexOptions.Singleline | RegexOptions.IgnoreCase,
        TimeSpan.FromSeconds(5));

    private static readonly Lazy<Dictionary<string, RenderedEmail>> _templates = new(Read);

    /// <summary>The cultures this mail has a template for, which is what makes them available.</summary>
    internal static IReadOnlyCollection<string> AvailableCultures => _templates.Value.Keys;

    internal static RenderedEmail Create(
        string languageTag,
        IReadOnlyDictionary<string, string> placeholders)
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

    private static RenderedEmail Best(string languageTag)
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
                $"'{_baseName}' has no template for the fallback culture '{FallbackCulture}'.");
    }

    private static Dictionary<string, RenderedEmail> Read()
    {
        var assembly = typeof(ReminderEmailTemplate).Assembly;
        var found = new Dictionary<string, RenderedEmail>(StringComparer.OrdinalIgnoreCase);
        const string prefix = $".{_baseName}.";

        foreach (string resource in assembly.GetManifestResourceNames())
        {
            int start = resource.IndexOf(prefix, StringComparison.Ordinal);

            if (start < 0 || !resource.EndsWith(".html", StringComparison.Ordinal))
            {
                continue;
            }

            string culture = resource[(start + prefix.Length)..^".html".Length];

            if (culture.Length == 0 || culture.Contains('.', StringComparison.Ordinal))
            {
                continue;
            }

            using var reader = new StreamReader(
                assembly.GetManifestResourceStream(resource)
                ?? throw new InvalidOperationException($"'{resource}' could not be opened."));

            string text = reader.ReadToEnd();
            var title = _title.Match(text);

            if (!title.Success || string.IsNullOrWhiteSpace(title.Groups["subject"].Value))
            {
                throw new InvalidOperationException(
                    $"The embedded email template '{resource}' has no non-empty <title>, which is the "
                    + "mail's subject.");
            }

            found[culture] = new RenderedEmail(
                WebUtility.HtmlDecode(title.Groups["subject"].Value).Trim(),
                text);
        }

        return found.Count > 0
            ? found
            : throw new InvalidOperationException(
                $"No embedded template matched '{_baseName}.<culture>.html' in {assembly.GetName().Name}.");
    }
}
