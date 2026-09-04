using System.Text;

namespace AppTemplate.Infrastructure.Persistence.Features.TodoLists.Queries;

/// <summary>
/// Turns free text into a safe <c>ILIKE</c> "contains" pattern. Unescaped, a caller's <c>%</c> would
/// match every row and a run of <c>_</c> would perform a wildcard scan rather than a literal search —
/// this is a safety property, not cosmetics.
/// </summary>
internal static class TodoListLikePattern
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
