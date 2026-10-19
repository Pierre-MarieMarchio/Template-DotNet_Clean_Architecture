using AppTemplate.Api.Common.Contracts;
using AppTemplate.Api.Features.Notes.Contracts.Responses;
using AppTemplate.Application.Core.Common.Concurrency;
using AppTemplate.Application.Core.Common.Results;
using AppTemplate.Application.Features.Notes.Model.Dtos;

namespace AppTemplate.Api.Features.Notes.Contracts.Mapping;

/// <summary>
/// Projects the feature's application DTOs onto its wire contracts. Positional records plus
/// <c>TreatWarningsAsErrors</c> make a member added on either side fail the build here.
/// </summary>
internal static class NoteResponseMapping
{
    public static NoteResponse ToResponse(NoteDto note)
    {
        ArgumentNullException.ThrowIfNull(note);

        return new NoteResponse(note.Id, note.Title, note.Body, note.CreatedAt, note.LastModifiedAt);
    }

    public static NoteSummaryResponse ToResponse(NoteSummaryDto summary)
    {
        ArgumentNullException.ThrowIfNull(summary);

        return new NoteSummaryResponse(summary.Id, summary.Title, summary.CreatedAt, summary.LastModifiedAt);
    }

    public static Result<PagedResponse<NoteSummaryResponse>> ToPageResponse(
        Result<PagedResult<NoteSummaryDto>> result) =>
        result.Map(value => PagedResponse.From(value, ToResponse));

    public static Result<Versioned<NoteResponse>> ToNoteResponse(Result<Versioned<NoteDto>> result) =>
        result.Map(value => new Versioned<NoteResponse>(ToResponse(value.Value), value.Version));
}
