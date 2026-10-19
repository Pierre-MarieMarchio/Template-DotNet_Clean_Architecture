using AppTemplate.Application.Core.Common.Collections;

namespace AppTemplate.Application.Features.Notes.Model.Dtos;

/// <param name="Paging">"offset" (the default) or "cursor". Blank means offset.</param>
/// <param name="Page">1-based page number. Offset mode only.</param>
/// <param name="PageSize">Defaults to the policy's own default page size; must not exceed its ceiling.</param>
/// <param name="Cursor">Opaque, minted by a previous page's <c>nextCursor</c>. Cursor mode only.</param>
/// <param name="Sort">Comma-separated sort terms, e.g. <c>title:asc,createdAt:desc</c>. Cursor mode allows one.</param>
public sealed record NoteListQueryDto(
    string? Paging,
    int? Page,
    int? PageSize,
    string? Cursor,
    string? Sort) : ICollectionQuery
{
    /// <summary>The common case: offset paging, nothing sorted or resumed.</summary>
    public static NoteListQueryDto Offset(int? page, int? pageSize) => new(null, page, pageSize, null, null);
}
