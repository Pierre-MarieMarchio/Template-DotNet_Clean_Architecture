using AppTemplate.Api.Common.Controllers;
using AppTemplate.Api.Features.Files.Contracts.Mapping;
using AppTemplate.Api.Features.Files.Contracts.Requests;
using AppTemplate.Api.Features.Files.Contracts.Responses;
using AppTemplate.Application.Features.Files.UseCases.Commands.ReplaceStoredFileTags;
using AppTemplate.Application.Features.Files.UseCases.Queries.GetUsedFileTags;
using Microsoft.AspNetCore.Mvc;

namespace AppTemplate.Api.Features.Files.Controllers;

[Route("api/v{version:apiVersion}/files")]
[Asp.Versioning.ApiVersion("1.0")]
[ProducesResponseType(StatusCodes.Status401Unauthorized, Type = typeof(ProblemDetails))]
public sealed class FileTagsController(
    IReplaceStoredFileTagsUseCase replaceStoredFileTags,
    IGetUsedFileTagsUseCase getUsedTags) : ApiControllerBase
{
    /// <summary>Replaces the labels a file carries.</summary>
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
    [HttpGet("tags")]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(UsedTagsResponse))]
    public async Task<ActionResult<UsedTagsResponse>> GetUsedTags(
        CancellationToken cancellationToken = default) =>
        OkOrProblem(StoredFileResponseMapping.ToUsedTagsResponse(
            await getUsedTags.ExecuteAsync(cancellationToken)));
}
