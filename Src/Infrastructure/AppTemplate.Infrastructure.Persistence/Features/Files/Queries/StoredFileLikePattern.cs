using System.Text;

namespace AppTemplate.Infrastructure.Persistence.Features.Files.Queries;

/// <summary>Turns free text into a safe <c>ILIKE</c> "contains" pattern: unescaped, a caller's <c>%</c> would match every row and <c>_</c> would become a wildcard.</summary>
/// <remarks>
/// A twin of <c>TodoListLikePattern</c> rather than a shared helper, so that deleting the example
/// to-do feature cannot break this search. Promote it to <c>Common/</c> when a third feature needs it.
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
