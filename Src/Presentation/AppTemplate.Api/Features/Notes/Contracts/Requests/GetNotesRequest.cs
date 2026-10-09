namespace AppTemplate.Api.Features.Notes.Contracts.Requests;

/// <summary>
/// Bound from the query string. <c>page</c> and <c>pageSize</c> are typed as <see cref="int"/>?, so a
/// value that is not a number is answered by model binding with <c>request.validationFailed</c>;
/// every other violation of the collection contract is decided by the application layer under its
/// own specific code.
/// </summary>
public sealed record GetNotesRequest(
    string? Paging,
    int? Page,
    int? PageSize,
    string? Cursor,
    string? Sort);
