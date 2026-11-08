namespace AppTemplate.Api.Features.Notes.Contracts.Requests;

/// <summary>The paging, sorting and filters of a note listing.</summary>
public sealed record GetNotesRequest(
    string? Paging,
    int? Page,
    int? PageSize,
    string? Cursor,
    string? Sort);
