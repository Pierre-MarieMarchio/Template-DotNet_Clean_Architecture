using AppTemplate.Api.Common.Controllers;
using AppTemplate.Api.Features.Files.Contracts.Mapping;
using AppTemplate.Api.Features.Files.Contracts.Requests;
using AppTemplate.Api.Features.Files.Contracts.Responses;
using AppTemplate.Application.Features.Files.UseCases.Commands.ReplaceStoredFileTags;
using AppTemplate.Application.Features.Files.UseCases.Queries.GetUsedFileTags;
using Microsoft.AspNetCore.Mvc;

namespace AppTemplate.Api.Features.Files.Controllers;

/// <summary>
/// The labels a stored file carries, and the labels a caller has already used.
/// </summary>
[Route("api/v{version:apiVersion}/files")]
[Asp.Versioning.ApiVersion("1.0")]
[ProducesResponseType(StatusCodes.Status401Unauthorized, Type = typeof(ProblemDetails))]
public sealed class FileTagsController(
    IReplaceStoredFileTagsUseCase replaceStoredFileTags,
    IGetUsedFileTagsUseCase getUsedTags) : ApiControllerBase
{
    /// <summary>Replaces the labels a file carries.</summary>
    /// <remarks>
    /// <c>PUT</c> of the whole set: an omitted tag is a tag removed, and an empty list clears them.
    /// <c>If-Match</c> on the version read turns two clients relabelling the same file into a 412 for
    /// the loser. Normalisation, de-duplication and the cap are the domain's, shared with to-do items.
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
    /// For a picker or a filter. Cached briefly, so a tag added moments ago may be missing.
    /// </remarks>
    [HttpGet("tags")]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(UsedTagsResponse))]
    public async Task<ActionResult<UsedTagsResponse>> GetUsedTags(
        CancellationToken cancellationToken = default) =>
        OkOrProblem(StoredFileResponseMapping.ToUsedTagsResponse(
            await getUsedTags.ExecuteAsync(cancellationToken)));
}
