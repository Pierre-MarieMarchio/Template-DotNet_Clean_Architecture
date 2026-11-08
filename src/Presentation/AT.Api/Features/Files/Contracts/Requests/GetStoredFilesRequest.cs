namespace AppTemplate.Api.Features.Files.Contracts.Requests;

/// <summary>The paging, sorting and filters of a file listing.</summary>
/// <param name="State">
/// <c>pending</c>, <c>deposited</c>, <c>available</c> or <c>quarantined</c>; blank means all four.
/// </param>
public sealed record GetStoredFilesRequest(
    string? Paging,
    int? Page,
    int? PageSize,
    string? Cursor,
    string? Sort,
    string? Search,
    string? State);
