using AppTemplate.Domain.Core.Common.Primitives;
namespace AppTemplate.Application.Features.TodoLists.Ports.TodoItemTagQueries;

public interface ITodoItemTagQueries
{
    Task<IReadOnlyList<string>> GetUsedTagsForOwnerAsync(
        UserId ownerId,
        CancellationToken cancellationToken = default);
}
