namespace AppTemplate.Api.Features.TodoLists.Contracts.Requests;

/// <summary>
/// Bound from the query string.
/// </summary>
/// <remarks>
/// <c>page</c> and <c>pageSize</c> are typed as <see cref="int"/>?, so a value that is not a number
/// never reaches the controller: model binding answers 400 with <c>request.validationFailed</c> and
/// names the offending field in <c>errors</c>. That is the same code an Application-layer validation
/// failure carries, deliberately — see <c>ModelStateProblemExtensions</c>, which argues that a
/// client should not have to tell a rejected shape from a rejected value to know what to do.
/// Everything else named here — an unknown sort field, a filter out of bounds, a bad cursor, a page
/// past the ceiling — is a contract violation the Application layer decides on, and comes back under
/// its own specific code, because those a client can act on differently.
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
