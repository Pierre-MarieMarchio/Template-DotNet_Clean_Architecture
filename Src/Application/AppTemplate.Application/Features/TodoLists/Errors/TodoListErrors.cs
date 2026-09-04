using AppTemplate.Application.Common;

namespace AppTemplate.Application.Features.TodoLists.Errors;

public static class TodoListErrors
{
    /// <summary>
    /// Also returned when the list belongs to somebody else: distinguishing the two would let a
    /// caller enumerate other users' list ids by comparing 403 against 404.
    /// </summary>
    public static Error ListNotFound(Guid todoListId) => Error.NotFound(
        "todoList.notFound",
        $"No to-do list with id '{todoListId}' was found.");

    public static Error ItemNotFound(Guid todoItemId) => Error.NotFound(
        "todoItem.notFound",
        $"No to-do item with id '{todoItemId}' was found on this list.");

    /// <summary>
    /// Only reachable where unconditional writes are refused by configuration; the message names the
    /// header, because a client that never sent one has no other way to learn what is missing.
    /// </summary>
    public static readonly Error IfMatchRequired = Error.PreconditionRequired(
        "precondition.required",
        "This operation requires an 'If-Match' header carrying the entity tag of the version the "
        + "request was decided against.");

    public static readonly Error MalformedIfMatch = Error.Validation(
        "precondition.malformed",
        "The 'If-Match' header is not '*' or a comma-separated list of quoted entity tags.");
}
