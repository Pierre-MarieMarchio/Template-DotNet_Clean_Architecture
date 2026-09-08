namespace AppTemplate.Application.Features.TodoLists.Ports.TodoItemTagQueries;

/// <summary>
/// What this owner has labelled their items with. One capability of its own rather than a fifth
/// method on the list queries: reading a list and reading the vocabulary the owner has built up are
/// different questions, and only one of them is worth caching.
/// </summary>
public interface ITodoItemTagQueries
{
    /// <summary>
    /// The distinct tags this owner has put on their items. Counted in the database rather than by
    /// loading their lists: the answer is one column of a join, and materialising every item to
    /// collect it would cost more than the picker it fills.
    /// </summary>
    /// <returns>Every tag value in use, each once, ordered so two calls agree.</returns>
    Task<IReadOnlyList<string>> GetUsedTagsForOwnerAsync(
        Guid ownerId,
        CancellationToken cancellationToken = default);
}
