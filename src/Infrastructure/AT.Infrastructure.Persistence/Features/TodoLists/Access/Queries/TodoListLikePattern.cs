using System.Text;

namespace AppTemplate.Infrastructure.Persistence.Features.TodoLists.Access.Queries;

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
