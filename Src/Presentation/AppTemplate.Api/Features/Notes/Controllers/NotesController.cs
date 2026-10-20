using AppTemplate.Api.Common.Contracts;
using AppTemplate.Api.Common.Controllers;
using AppTemplate.Api.Common.Idempotency;
using AppTemplate.Api.Features.Notes.Contracts.Mapping;
using AppTemplate.Api.Features.Notes.Contracts.Requests;
using AppTemplate.Api.Features.Notes.Contracts.Responses;
using AppTemplate.Application.Features.Notes.Model.Dtos;
using AppTemplate.Application.Features.Notes.Services;
using Microsoft.AspNetCore.Mvc;

namespace AppTemplate.Api.Features.Notes.Controllers;

[Route("api/v{version:apiVersion}/notes")]
[Asp.Versioning.ApiVersion("1.0")]
[ProducesResponseType(StatusCodes.Status401Unauthorized, Type = typeof(ProblemDetails))]
public sealed class NotesController(INoteService notes) : ApiControllerBase
{
    /// <summary>Lists the caller's own notes, sorted and paginated.</summary>
    [HttpGet]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(PagedResponse<NoteSummaryResponse>))]
    [ProducesResponseType(StatusCodes.Status400BadRequest, Type = typeof(ValidationProblemDetails))]
    public async Task<ActionResult<PagedResponse<NoteSummaryResponse>>> GetAll(
        [FromQuery] GetNotesRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var query = new NoteListQueryDto(request.Paging, request.Page, request.PageSize, request.Cursor, request.Sort);

        return OkOrProblem(NoteResponseMapping.ToPageResponse(await notes.ListAsync(query, cancellationToken)));
    }

    /// <summary>Gets one note, and the <c>ETag</c> needed to change it.</summary>
    [HttpGet("{noteId:guid}", Name = nameof(GetNote))]
    [HttpHead("{noteId:guid}")]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(NoteResponse))]
    [ProducesResponseType(StatusCodes.Status304NotModified)]
    [ProducesResponseType(StatusCodes.Status404NotFound, Type = typeof(ProblemDetails))]
    public async Task<ActionResult<NoteResponse>> GetNote(Guid noteId, CancellationToken cancellationToken)
    {
        return OkOrProblem(NoteResponseMapping.ToNoteResponse(await notes.GetAsync(noteId, cancellationToken)));
    }

    /// <summary>Creates a note owned by the caller.</summary>
    [HttpPost]
    [Idempotent]
    [ProducesResponseType(StatusCodes.Status201Created, Type = typeof(NoteResponse))]
    [ProducesResponseType(StatusCodes.Status400BadRequest, Type = typeof(ValidationProblemDetails))]
    [ProducesResponseType(StatusCodes.Status409Conflict, Type = typeof(ProblemDetails))]
    public async Task<ActionResult<NoteResponse>> Create(
        [FromBody] CreateNoteRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var result = NoteResponseMapping.ToNoteResponse(
            await notes.CreateAsync(new NoteContentDto(request.Title, request.Body), cancellationToken));

        return CreatedOrProblem(result, nameof(GetNote), note => new { noteId = note.Id });
    }

    /// <summary>Replaces a note's title and body.</summary>
    [HttpPut("{noteId:guid}")]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(NoteResponse))]
    [ProducesResponseType(StatusCodes.Status400BadRequest, Type = typeof(ValidationProblemDetails))]
    [ProducesResponseType(StatusCodes.Status404NotFound, Type = typeof(ProblemDetails))]
    [ProducesResponseType(StatusCodes.Status409Conflict, Type = typeof(ProblemDetails))]
    [ProducesResponseType(StatusCodes.Status412PreconditionFailed, Type = typeof(ProblemDetails))]
    [ProducesResponseType(StatusCodes.Status428PreconditionRequired, Type = typeof(ProblemDetails))]
    public async Task<ActionResult<NoteResponse>> Update(
        Guid noteId,
        [FromBody] UpdateNoteRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (ReadPrecondition(out var precondition, out bool requiresExistence) is { } refusal)
        {
            return refusal;
        }

        var result = RequiringExistence(
            requiresExistence,
            await notes.UpdateAsync(noteId, new NoteContentDto(request.Title, request.Body), precondition, cancellationToken));

        return UpdatedOrProblem(NoteResponseMapping.ToNoteResponse(result));
    }

    /// <summary>Deletes a note, on the same <c>If-Match</c> terms as the update.</summary>
    [HttpDelete("{noteId:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status400BadRequest, Type = typeof(ValidationProblemDetails))]
    [ProducesResponseType(StatusCodes.Status404NotFound, Type = typeof(ProblemDetails))]
    [ProducesResponseType(StatusCodes.Status409Conflict, Type = typeof(ProblemDetails))]
    [ProducesResponseType(StatusCodes.Status412PreconditionFailed, Type = typeof(ProblemDetails))]
    [ProducesResponseType(StatusCodes.Status428PreconditionRequired, Type = typeof(ProblemDetails))]
    public async Task<ActionResult> Delete(Guid noteId, CancellationToken cancellationToken)
    {
        if (ReadPrecondition(out var precondition, out bool requiresExistence) is { } refusal)
        {
            return refusal;
        }

        var result = RequiringExistence(
            requiresExistence,
            await notes.DeleteAsync(noteId, precondition, cancellationToken));

        return NoContentOrProblem(result);
    }
}
