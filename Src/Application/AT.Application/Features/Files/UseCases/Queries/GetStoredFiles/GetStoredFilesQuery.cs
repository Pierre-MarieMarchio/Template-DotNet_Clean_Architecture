using AppTemplate.Application.Core.Common.Collections;

namespace AppTemplate.Application.Features.Files.UseCases.Queries.GetStoredFiles;

public sealed record GetStoredFilesQuery(
    string? Paging,
    int? Page,
    int? PageSize,
    string? Cursor,
    string? Sort,
    string? Search,
    string? State) : ICollectionQuery
{
    public static GetStoredFilesQuery Offset(int? page, int? pageSize) =>
        new(null, page, pageSize, null, null, null, null);
}
