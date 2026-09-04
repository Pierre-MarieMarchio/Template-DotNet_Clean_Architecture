using System.Text;

namespace AppTemplate.Infrastructure.Persistence.Features.Files.Queries;

/// <summary>
/// Turns free text into a safe <c>ILIKE</c> "contains" pattern. Unescaped, a caller's <c>%</c> would
/// match every row and a run of <c>_</c> would perform a wildcard scan rather than a literal search —
/// this is a safety property, not cosmetics.
/// </summary>
/// <remarks>
/// A deliberate twin of <c>TodoListLikePattern</c> rather than a shared helper. The two are the first
/// pair of cases that agree, and <c>CONTRIBUTING.md</c>'s rule is to extract what two real cases prove
/// identical; the reason this one stays is different and stronger — a helper shared between the two
/// would make deleting the example to-do feature break the file feature's search, which is exactly the
/// coupling per-feature folders exist to prevent. Promote it to <c>Common/</c> the day a third feature
/// needs it, not the day two do.
/// </remarks>
internal static class StoredFileLikePattern
{
    private const char _escape = '\\';

    public static string Contains(string term)
    {
        ArgumentNullException.ThrowIfNull(term);

        var pattern = new StringBuilder(term.Length + 2).Append('%');

        foreach (char character in term)
        {
            if (character is '\\' or '%' or '_')
            {
                pattern.Append(_escape);
            }

            pattern.Append(character);
        }

        return pattern.Append('%').ToString();
    }
}
