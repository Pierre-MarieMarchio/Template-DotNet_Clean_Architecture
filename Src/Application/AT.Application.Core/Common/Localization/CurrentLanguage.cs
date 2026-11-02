using System.Text.RegularExpressions;

namespace AppTemplate.Application.Core.Common.Localization;

public static class CurrentLanguage
{
    public const string FallbackTag = "en";

    private static readonly Regex _wellFormed = new(
        @"^[A-Za-z]{2,3}(-[A-Za-z0-9]{2,8})*$",
        RegexOptions.None,
        TimeSpan.FromSeconds(1));

    private static readonly AsyncLocal<string?> _tag = new();

    private static string _default = FallbackTag;

    public static string Default
    {
        get => _default;
        set => _default = IsWellFormed(value) ? value : throw new ArgumentException(
            $"'{value}' is not a well-formed language tag.", nameof(value));
    }

    public static string Current => _tag.Value ?? _default;

    public static string? Tag
    {
        get => _tag.Value;
        set => _tag.Value = value is not null && IsWellFormed(value) ? value : null;
    }

    public static bool IsWellFormed(string? tag) =>
        !string.IsNullOrWhiteSpace(tag) && tag.Length <= 35 && _wellFormed.IsMatch(tag);

    public static IEnumerable<string> Candidates(string tag)
    {
        ArgumentNullException.ThrowIfNull(tag);

        return Narrow(tag);

        static IEnumerable<string> Narrow(string tag)
        {
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
}
