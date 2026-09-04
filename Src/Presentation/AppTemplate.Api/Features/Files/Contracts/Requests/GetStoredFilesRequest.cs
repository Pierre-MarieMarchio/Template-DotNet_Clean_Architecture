namespace AppTemplate.Api.Features.Files.Contracts.Requests;

/// <summary>
/// Bound from the query string.
/// </summary>
/// <remarks>
/// <c>page</c> and <c>pageSize</c> are <see cref="int"/>?, so a value that is not a number is
/// refused by model binding with the framework's own <c>request.malformed</c> code — a shape error,
/// not a rule the caller broke. Everything else here (an unknown sort field, a state that is
/// neither <c>pending</c> nor <c>available</c>, a cursor minted under another order) is decided by
/// the Application layer and comes back under its own code.
/// </remarks>
/// <param name="State">
/// <c>pending</c> or <c>available</c>; blank means both. The same two words the response's
/// <c>status</c> carries, so a client filters with the value it just read rather than translating
/// between two vocabularies.
/// </param>
public sealed record GetStoredFilesRequest(
    string? Paging,
    int? Page,
    int? PageSize,
    string? Cursor,
    string? Sort,
    string? Search,
    string? State);
