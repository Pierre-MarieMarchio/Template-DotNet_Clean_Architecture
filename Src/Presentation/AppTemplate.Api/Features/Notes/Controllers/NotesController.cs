using AppTemplate.Api.Common.Contracts;
using AppTemplate.Api.Common.Controllers;
using AppTemplate.Api.Common.Idempotency;
using AppTemplate.Api.Features.Notes.Contracts.Requests;
using AppTemplate.Api.Features.Notes.Contracts.Responses;
using AppTemplate.Api.Features.Notes.Mapping;
using AppTemplate.Application.Features.Notes.Dtos;
using AppTemplate.Application.Features.Notes.Services;
using Microsoft.AspNetCore.Mvc;

namespace AppTemplate.Api.Features.Notes.Controllers;

/// <summary>The caller's own notes: the collection, one note, and the three writes that create, replace and delete one.</summary>
/// <remarks>
/// A single note publishes its version as a strong <c>ETag</c>, and its update and delete honour
/// <c>If-Match</c>. The feature has no domain model, so one service answers every route; see
/// <c>docs/ADDING-A-FEATURE.md</c> for when that shape is the right one.
/// </remarks>
[Route("api/v{version:apiVersion}/notes")]
[Asp.Versioning.ApiVersion("1.0")]
[ProducesResponseType(StatusCodes.Status401Unauthorized, Type = typeof(ProblemDetails))]
public sealed class NotesController(INoteService notes) : ApiControllerBase
{
    /// <summary>Lists the caller's own notes, sorted and paginated.</summary>
    /// <remarks>
    /// Two paging modes: <c>offset</c> (the default), addressed by <c>page</c>/<c>pageSize</c> and
    /// answering a <c>totalCount</c>; and <c>cursor</c>, addressed by an opaque <c>cursor</c> token
    /// minted by the previous page's <c>nextCursor</c>, which never counts the whole match set.
    /// <c>sort</c> is a comma-separated list of whitelisted fields (<c>title</c>, <c>createdAt</c>),
    /// each optionally suffixed <c>:asc</c>/<c>:desc</c>; cursor mode allows at most one. A page
    /// carries titles only, not bodies.
    /// <para>
    /// No <c>ETag</c>: a page of summaries is not one note, so there is no single version that
    /// describes it and no write that <c>If-Match</c> could guard.
    /// </para>
    /// </remarks>
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
    /// <remarks>
    /// No <c>If-Match</c>: the resource does not exist yet, so there is no version to name.
    /// <para>
    /// Idempotent: send an <c>Idempotency-Key</c> header to make a retried request safe. Repeating
    /// the same key with the same body returns the first response again, carrying
    /// <c>Idempotency-Replayed: true</c>, instead of creating a second note; repeating it with a
    /// different body is refused with <c>idempotency.keyReused</c>.
    /// </para>
    /// </remarks>
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
    /// <remarks>
    /// Honours <c>If-Match</c>: a version the note no longer holds is refused with 412, and the write
    /// itself is conditioned on the version it read, so a concurrent change that lands first is
    /// refused with 409 rather than overwritten. Without <c>If-Match</c> the update is unconditional
    /// unless <c>Concurrency:IfMatch</c> is <c>Required</c>.
    /// </remarks>
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

        // The id comes from the route, never the body, so two sources of truth cannot disagree.
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
