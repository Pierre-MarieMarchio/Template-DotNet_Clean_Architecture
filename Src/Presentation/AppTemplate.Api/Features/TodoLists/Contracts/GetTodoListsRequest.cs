namespace AppTemplate.Api.Features.TodoLists.Contracts;

/// <summary>
/// Bound from the query string.
/// </summary>
/// <remarks>
/// <c>page</c> and <c>pageSize</c> are typed as <see cref="int"/>?, so a value that is not a number
/// never reaches the controller: model binding answers 400 with the framework's own
/// <c>request.malformed</c> code, because that failure is about the shape of the request, not its
/// content. Everything else named here — an unknown sort field, a filter out of bounds, a bad
/// cursor, a page past the ceiling — is a contract violation the Application layer decides on, and
/// comes back as its own specific code. One vocabulary for a type error, one for a rule the caller
/// broke.
/// </remarks>
public sealed record GetTodoListsRequest(
    string? Paging,
    int? Page,
    int? PageSize,
    string? Cursor,
    string? Sort,
    string? Search,
    string? CreatedAfter,
    string? CreatedBefore);
