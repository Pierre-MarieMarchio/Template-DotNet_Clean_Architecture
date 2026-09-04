using System.Text.RegularExpressions;

namespace AppTemplate.Application.Common.Localization;

/// <summary>
/// The language the mail this flow is about should be written in, as a BCP-47 tag.
/// </summary>
/// <remarks>
/// <para>
/// <b>This is what <c>CultureInfo.CurrentUICulture</c> would be, and cannot be here.</b> The
/// repository builds with <c>InvariantGlobalization=true</c> — see <c>Directory.Build.props</c>, and
/// the API's Dockerfile, whose runtime image ships no ICU because of it — so at run time the only
/// culture that exists is the invariant one and <c>CultureInfo.GetCultureInfo("fr")</c> throws.
/// Carrying a tag rather than a culture is what lets this template pick a language without asking a
/// deployment to add ICU to its image.
/// </para>
/// <para>
/// It is ambient rather than a parameter for the reason the platform's own equivalent is: the
/// language is a property of the exchange, not of any one call, and threading it through every use
/// case and every mail port would put a parameter nothing in between reads on a dozen signatures.
/// It lives in the application layer because two infrastructure modules read it and a module may not
/// reference a sibling.
/// </para>
/// <para>
/// <see cref="Default"/> is set once at start-up and <see cref="Tag"/> per request, so a host with
/// no requests still has an answer and a request that names no language falls back to one.
/// </para>
/// </remarks>
public static class CurrentLanguage
{
    /// <summary>
    /// The language every mail is written in when nothing more specific is known — for
    /// <c>AppTemplate.Worker</c>, that is every mail it sends.
    /// </summary>
    public const string FallbackTag = "en";

    private static readonly Regex _wellFormed = new(
        @"^[A-Za-z]{2,3}(-[A-Za-z0-9]{2,8})*$",
        RegexOptions.None,
        TimeSpan.FromSeconds(1));

    private static readonly AsyncLocal<string?> _tag = new();

    private static string _default = FallbackTag;

    /// <summary>
    /// The host's own default, set once at start-up from <c>Localization:DefaultCulture</c>. Not
    /// <see cref="AsyncLocal{T}"/>, deliberately: a value set during start-up would not flow into
    /// every hosted service's own execution context, and a worker whose loops silently reverted to
    /// English is the failure this is arranged to avoid.
    /// </summary>
    public static string Default
    {
        get => _default;
        set => _default = IsWellFormed(value) ? value : throw new ArgumentException(
            $"'{value}' is not a well-formed language tag.", nameof(value));
    }

    /// <summary>The language of the exchange in progress, falling back to <see cref="Default"/>.</summary>
    public static string Current => _tag.Value ?? _default;

    /// <summary>
    /// Set per request by the API. A malformed tag is ignored rather than refused: it arrives in a
    /// header a caller controls, and a mail in the wrong language is a better answer than a failed
    /// request.
    /// </summary>
    public static string? Tag
    {
        get => _tag.Value;
        set => _tag.Value = value is not null && IsWellFormed(value) ? value : null;
    }

    /// <summary>
    /// Shape only. Whether a tag names a language this deployment can write is not a question this
    /// type answers — that is which templates are embedded, and the renderers fall back on their own.
    /// </summary>
    public static bool IsWellFormed(string? tag) =>
        !string.IsNullOrWhiteSpace(tag) && tag.Length <= 35 && _wellFormed.IsMatch(tag);

    /// <summary>
    /// The tag itself, then each broader tag in turn — <c>fr-CA</c> then <c>fr</c> — so a regional
    /// reader reaches their language's template without every region needing one of its own.
    /// </summary>
    public static IEnumerable<string> Candidates(string tag)
    {
        ArgumentNullException.ThrowIfNull(tag);

        for (int cut = tag.Length; cut > 0; cut = tag.LastIndexOf('-', cut - 1))
        {
            yield return tag[..cut];

            if (!tag[..cut].Contains('-', StringComparison.Ordinal))
            {
                yield break;
            }
        }
    }
}
