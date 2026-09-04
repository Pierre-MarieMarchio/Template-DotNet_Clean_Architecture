using AppTemplate.Application.Common;
using FluentValidation.Results;

namespace AppTemplate.Application.Features.TodoLists.Errors;

public static class TodoListErrors
{
    public static readonly Error NotAuthenticated = Error.Unauthorized(
        "auth.required",
        "This operation requires an authenticated user.");

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
    /// <paramref name="message"/> is the <c>DomainException</c> text, returned to the client
    /// verbatim: only pass messages the aggregate authored, never a provider's.
    /// </summary>
    public static Error InvariantViolated(string message) => Error.Conflict(
        "todoList.invariantViolated",
        message);

    /// <summary>
    /// The caller's change was decided against a version the aggregate no longer holds. Deliberately
    /// distinct from <c>concurrency.conflict</c>, which is a race the caller could not have seen:
    /// this one means the caller was working from a stale copy and has to read again.
    /// </summary>
    public static readonly Error PreconditionFailed = Error.PreconditionFailed(
        "precondition.failed",
        "The resource has changed since the version this request names. Read it again and retry.");

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

    public static Error InvalidPaging(string message) => Error.Validation("paging.invalid", message);

    public static Error Invalid(ValidationResult validationResult)
    {
        ArgumentNullException.ThrowIfNull(validationResult);

        return Error.Validation(
            "todoList.validationFailed",
            string.Join(" ", validationResult.Errors.Select(failure => failure.ErrorMessage)));
    }
}
