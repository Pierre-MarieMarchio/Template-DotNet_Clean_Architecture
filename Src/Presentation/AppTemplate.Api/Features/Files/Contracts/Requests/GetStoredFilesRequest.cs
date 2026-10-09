namespace AppTemplate.Api.Features.Files.Contracts.Requests;

/// <summary>Bound from the query string.</summary>
/// <remarks>
/// <c>page</c> and <c>pageSize</c> are nullable so a non-number is refused by model binding under
/// <c>request.validationFailed</c>; every other refusal (unknown sort field, unknown state, a cursor
/// minted under another order) comes from the Application layer under its own code.
/// </remarks>
/// <param name="State">
/// <c>pending</c>, <c>deposited</c>, <c>available</c> or <c>quarantined</c>; blank means all four,
/// the same words as the response's <c>status</c>.
/// </param>
public sealed record GetStoredFilesRequest(
    string? Paging,
    int? Page,
    int? PageSize,
    string? Cursor,
    string? Sort,
    string? Search,
    string? State);
