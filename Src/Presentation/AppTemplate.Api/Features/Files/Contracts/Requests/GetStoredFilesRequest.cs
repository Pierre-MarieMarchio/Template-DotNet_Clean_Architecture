namespace AppTemplate.Api.Features.Files.Contracts.Requests;

/// <summary>
/// Bound from the query string.
/// </summary>
/// <remarks>
/// <c>page</c> and <c>pageSize</c> are <see cref="int"/>?, so a value that is not a number is
/// refused by model binding with <c>request.validationFailed</c>, the same code an Application-layer
/// validation failure carries — see <c>ModelStateProblemExtensions</c> for why the two are one
/// vocabulary. Everything else here (an unknown sort field, a state outside the four the domain has,
/// a cursor minted under another order) is decided by the Application layer and comes back under its
/// own code.
/// </remarks>
/// <param name="State">
/// <c>pending</c>, <c>deposited</c>, <c>available</c> or <c>quarantined</c>; blank means all four.
/// The same words the response's <c>status</c> carries, so a client filters with the value it just
/// read rather than translating between two vocabularies.
/// </param>
public sealed record GetStoredFilesRequest(
    string? Paging,
    int? Page,
    int? PageSize,
    string? Cursor,
    string? Sort,
    string? Search,
    string? State);
