namespace AppTemplate.Application.Features.Files.UseCases.Queries.GetStoredFiles;

/// <param name="Paging">"offset" (the default) or "cursor". Blank means offset.</param>
/// <param name="Page">1-based page number. Offset mode only.</param>
/// <param name="PageSize">
/// Defaults to <see cref="Policies.StoredFileCollectionPolicy.Instance"/>'s own default page size;
/// must not exceed its ceiling.
/// </param>
/// <param name="Cursor">Opaque, minted by a previous page's <c>nextCursor</c>. Cursor mode only.</param>
/// <param name="Sort">
/// Comma-separated sort terms, e.g. <c>name:asc,registeredAt:desc</c>. Cursor mode allows at most one.
/// </param>
/// <param name="Search">Matches the file name, case-insensitively, as a contains.</param>
/// <param name="State">"pending" or "available". Blank means both.</param>
public sealed record GetStoredFilesQuery(
    string? Paging,
    int? Page,
    int? PageSize,
    string? Cursor,
    string? Sort,
    string? Search,
    string? State)
{
    /// <summary>The common case: offset paging, nothing sorted, filtered or resumed.</summary>
    public static GetStoredFilesQuery Offset(int? page, int? pageSize) =>
        new(null, page, pageSize, null, null, null, null);
}
