using AppTemplate.Domain.Core.Common.Primitives;
namespace AppTemplate.Application.Common.Tagging;

public static class UsedTagsCache
{
    public static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(1);

    public const string TodoItemScope = "todo-items";

    public const string FileScope = "files";

    public static string KeyFor(string scope, UserId ownerId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(scope);
        ArgumentNullException.ThrowIfNull(ownerId);

        return $"tags:{scope}:{ownerId}";
    }
}
