using AppTemplate.Application.Core.Common.Collections;

namespace AppTemplate.Application.Features.Notes.Model.Dtos;

public sealed record NoteListQueryDto(
    string? Paging,
    int? Page,
    int? PageSize,
    string? Cursor,
    string? Sort) : ICollectionQuery
{
    public static NoteListQueryDto Offset(int? page, int? pageSize) => new(null, page, pageSize, null, null);
}
