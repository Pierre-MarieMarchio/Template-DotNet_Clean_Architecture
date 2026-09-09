using AppTemplate.Api.Core.Common.Controllers;
using AppTemplate.Api.Features.Files.Contracts.Requests;
using AppTemplate.Api.Features.Files.Contracts.Responses;
using AppTemplate.Api.Features.Files.Mapping;
using AppTemplate.Application.Features.Files.UseCases.Commands.ReplaceStoredFileTags;
using AppTemplate.Application.Features.Files.UseCases.Queries.GetUsedFileTags;
using Microsoft.AspNetCore.Mvc;

namespace AppTemplate.Api.Features.Files.Controllers;

/// <summary>
/// The labels a stored file carries, and the labels a caller has already used.
/// </summary>
/// <remarks>
/// Beside <see cref="FilesController"/> rather than inside it, and the route prefix is the same, so
/// both paths below are what they always were. Tagging is the one part of this surface that is not
/// about a file's content or its deposit: it names no object key, moves no bytes and has no
/// two-request flow, and the file controller's own documentation is an argument about exactly those
/// three things.
/// <para>
/// The set's rules -- normalisation, de-duplication, the cap -- are the domain's, and a to-do item
/// obeys the same ones through the same code, which is why a refusal here reads the same as a
/// refusal there.
/// </para>
/// </remarks>
[Route("api/v{version:apiVersion}/files")]
[Asp.Versioning.ApiVersion("1.0")]
[ProducesResponseType(StatusCodes.Status401Unauthorized, Type = typeof(ProblemDetails))]
public sealed class FileTagsController(
    IReplaceStoredFileTagsUseCase replaceStoredFileTags,
    IGetUsedFileTagsUseCase getUsedTags) : ApiControllerBase
{
    /// <summary>Replaces the labels a file carries.</summary>
    /// <remarks>
    /// <b>PUT and not PATCH</b>, and the request carries the whole set: what a partial update of a
    /// set would mean is not a question this API answers, so an omitted tag is a tag removed and
    /// the body says so. Sending an empty list is how a caller clears them.
    /// <para>
    /// Conditional for the ordinary reason: two clients relabelling the same file unconditionally
    /// would each overwrite the other's set, and the loser would never learn. <c>If-Match</c> on
    /// the version the caller read turns that into a 412.
    /// </para>
    /// <para>
    /// The rules the set obeys — normalisation, de-duplication, the cap — are the domain's, and a
    /// to-do item obeys the same ones through the same code. A refusal here therefore reads the
    /// same as a refusal there.
    /// </para>
    /// </remarks>
    [HttpPut("{fileId:guid}/tags")]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(StoredFileResponse))]
    [ProducesResponseType(StatusCodes.Status400BadRequest, Type = typeof(ValidationProblemDetails))]
    [ProducesResponseType(StatusCodes.Status404NotFound, Type = typeof(ProblemDetails))]
    [ProducesResponseType(StatusCodes.Status409Conflict, Type = typeof(ProblemDetails))]
    [ProducesResponseType(StatusCodes.Status412PreconditionFailed, Type = typeof(ProblemDetails))]
    [ProducesResponseType(StatusCodes.Status428PreconditionRequired, Type = typeof(ProblemDetails))]
    public async Task<ActionResult<StoredFileResponse>> ReplaceTags(
        Guid fileId,
        ReplaceStoredFileTagsRequest request,
        CancellationToken cancellationToken)
    {
        if (ReadPrecondition(out var precondition, out bool requiresExistence) is { } refusal)
        {
            return refusal;
        }

        var command = new ReplaceStoredFileTagsCommand(fileId, request?.Tags ?? [], precondition);
        var result = RequiringExistence(
            requiresExistence,
            await replaceStoredFileTags.ExecuteAsync(command, cancellationToken));

        return UpdatedOrProblem(StoredFileResponseMapping.ToFileResponse(result));
    }

    /// <summary>The tags the caller has already used on their files.</summary>
    /// <remarks>
    /// For a picker or a filter. Served from a cache with a short lifetime, so a tag added
    /// moments ago may be missing: this is a suggestion, and no write depends on it.
    /// </remarks>
    [HttpGet("tags")]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(UsedTagsResponse))]
    public async Task<ActionResult<UsedTagsResponse>> GetUsedTags(
        CancellationToken cancellationToken = default) =>
        OkOrProblem(StoredFileResponseMapping.ToUsedTagsResponse(
            await getUsedTags.ExecuteAsync(cancellationToken)));
}
