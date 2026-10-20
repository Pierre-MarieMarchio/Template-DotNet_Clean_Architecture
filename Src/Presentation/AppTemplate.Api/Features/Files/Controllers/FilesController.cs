using AppTemplate.Api.Common.Caching;
using AppTemplate.Api.Common.Contracts;
using AppTemplate.Api.Common.Controllers;
using AppTemplate.Api.Common.Errors;
using AppTemplate.Api.Common.Hosting;
using AppTemplate.Api.Common.Idempotency;
using AppTemplate.Api.Features.Files.Contracts.Mapping;
using AppTemplate.Api.Features.Files.Contracts.Requests;
using AppTemplate.Api.Features.Files.Contracts.Responses;
using AppTemplate.Application.Core.Common.Validation;
using AppTemplate.Application.Features.Files.Configuration.Options;
using AppTemplate.Application.Features.Files.Model.Policies;
using AppTemplate.Application.Features.Files.UseCases.Commands.ConfirmFileUpload;
using AppTemplate.Application.Features.Files.UseCases.Commands.DeleteStoredFile;
using AppTemplate.Application.Features.Files.UseCases.Commands.RegisterFile;
using AppTemplate.Application.Features.Files.UseCases.Commands.UploadFile;
using AppTemplate.Application.Features.Files.UseCases.Queries.GetStoredFile;
using AppTemplate.Application.Features.Files.UseCases.Queries.GetStoredFiles;
using AppTemplate.Application.Features.Files.UseCases.Queries.IssueFileDownload;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace AppTemplate.Api.Features.Files.Controllers;

[Route("api/v{version:apiVersion}/files")]
[Asp.Versioning.ApiVersion("1.0")]
[ProducesResponseType(StatusCodes.Status401Unauthorized, Type = typeof(ProblemDetails))]
public sealed class FilesController(
    IGetStoredFilesUseCase getStoredFiles,
    IGetStoredFileUseCase getStoredFile,
    IIssueFileDownloadUseCase issueFileDownload,
    IRegisterFileUseCase registerFile,
    IUploadFileUseCase uploadFile,
    IConfirmFileUploadUseCase confirmFileUpload,
    IDeleteStoredFileUseCase deleteStoredFile) : ApiControllerBase
{
    /// <summary>Lists the caller's own files, sorted, filtered and paginated.</summary>
    [HttpGet]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(PagedResponse<StoredFileResponse>))]
    [ProducesResponseType(StatusCodes.Status400BadRequest, Type = typeof(ValidationProblemDetails))]
    public async Task<ActionResult<PagedResponse<StoredFileResponse>>> GetAll(
        [FromQuery] GetStoredFilesRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var query = new GetStoredFilesQuery(
            request.Paging,
            request.Page,
            request.PageSize,
            request.Cursor,
            request.Sort,
            request.Search,
            request.State);

        return OkOrProblem(
            StoredFileResponseMapping.ToPageResponse(await getStoredFiles.ExecuteAsync(query, cancellationToken)));
    }

    /// <summary>Gets one file's metadata, and the <c>ETag</c> needed to change it.</summary>
    [HttpGet("{fileId:guid}", Name = nameof(GetFile))]
    [HttpHead("{fileId:guid}")]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(StoredFileResponse))]
    [ProducesResponseType(StatusCodes.Status304NotModified)]
    [ProducesResponseType(StatusCodes.Status400BadRequest, Type = typeof(ValidationProblemDetails))]
    [ProducesResponseType(StatusCodes.Status404NotFound, Type = typeof(ProblemDetails))]
    public async Task<ActionResult<StoredFileResponse>> GetFile(Guid fileId, CancellationToken cancellationToken)
    {
        var query = new GetStoredFileQuery(fileId);

        return OkOrProblem(
            StoredFileResponseMapping.ToFileResponse(await getStoredFile.ExecuteAsync(query, cancellationToken)));
    }

    /// <summary>The file's content: a redirect to a signed URL, or the bytes themselves.</summary>
    [HttpGet("{fileId:guid}/content")]
    [NoStore]
    [ProducesResponseType(typeof(Stream), StatusCodes.Status200OK, "application/octet-stream")]
    [ProducesResponseType(StatusCodes.Status302Found)]
    [ProducesResponseType(StatusCodes.Status400BadRequest, Type = typeof(ValidationProblemDetails))]
    [ProducesResponseType(StatusCodes.Status404NotFound, Type = typeof(ProblemDetails))]
    [ProducesResponseType(StatusCodes.Status409Conflict, Type = typeof(ProblemDetails))]
    public async Task<ActionResult> GetContent(Guid fileId, CancellationToken cancellationToken)
    {
        var query = new IssueFileDownloadQuery(fileId);
        var result = await issueFileDownload.ExecuteAsync(query, cancellationToken);

        if (result.IsFailure)
        {
            return result.Error!.ToActionResult(HttpContext);
        }

        var download = result.Value;

        if (download.Redirect is { } grant)
        {
            return Redirect(grant.Url);
        }

        Response.Headers.ContentDisposition = DownloadDispositionPolicy.AttachmentFor(download.FileName);
        Response.Headers.XContentTypeOptions = "nosniff";

        return File(download.Content!, download.MediaType);
    }

    /// <summary>Reserves a place for a file and hands back the right to deposit its bytes.</summary>
    [HttpPost]
    [Idempotent]
    [NoStore]
    [ProducesResponseType(StatusCodes.Status201Created, Type = typeof(StoredFileRegistrationResponse))]
    [ProducesResponseType(StatusCodes.Status400BadRequest, Type = typeof(ValidationProblemDetails))]
    [ProducesResponseType(StatusCodes.Status409Conflict, Type = typeof(ProblemDetails))]
    public async Task<ActionResult<StoredFileRegistrationResponse>> Register(
        [FromBody] RegisterFileRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var command = new RegisterFileCommand(
            request.Category,
            request.Name,
            request.DeclaredMediaType,
            request.SizeInBytes,
            request.Checksum);

        var result = StoredFileResponseMapping.ToRegistrationResponse(
            await registerFile.ExecuteAsync(command, cancellationToken));

        if (result.IsFailure)
        {
            return result.Error!.ToActionResult(HttpContext);
        }

        return CreatedOrProblem(result, nameof(GetFile), new { fileId = result.Value.Id });
    }

    /// <summary>
    /// Uploads a file through the API: registers it, stores its bytes and confirms the deposit.
    /// </summary>
    [HttpPost("uploads")]
    [ApiUploadBodyLimit]
    [NoStore]
    [Consumes("multipart/form-data")]
    [ProducesResponseType(StatusCodes.Status201Created, Type = typeof(StoredFileResponse))]
    [ProducesResponseType(StatusCodes.Status400BadRequest, Type = typeof(ValidationProblemDetails))]
    [ProducesResponseType(StatusCodes.Status409Conflict, Type = typeof(ProblemDetails))]
    public async Task<ActionResult<StoredFileResponse>> Upload(
        [FromForm] UploadFileRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (request.File is not { } file || Request.Form.Files.Count != 1)
        {
            return ValidationError.ForField("file", "Exactly one file part is required.").ToActionResult(HttpContext);
        }

        await using var content = file.OpenReadStream();

        var command = new UploadFileCommand(
            request.Category ?? string.Empty,
            string.IsNullOrWhiteSpace(request.Name) ? file.FileName : request.Name,
            file.ContentType ?? string.Empty,
            file.Length,
            content);

        var result = StoredFileResponseMapping.ToFileResponse(await uploadFile.ExecuteAsync(command, cancellationToken));

        return CreatedOrProblem(result, nameof(GetFile), uploaded => new { fileId = uploaded.Id });
    }

    /// <summary>Confirms that the bytes have been deposited, moving the file to <c>deposited</c>.</summary>
    [HttpPost("{fileId:guid}/confirm")]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(StoredFileResponse))]
    [ProducesResponseType(StatusCodes.Status400BadRequest, Type = typeof(ValidationProblemDetails))]
    [ProducesResponseType(StatusCodes.Status404NotFound, Type = typeof(ProblemDetails))]
    [ProducesResponseType(StatusCodes.Status409Conflict, Type = typeof(ProblemDetails))]
    [ProducesResponseType(StatusCodes.Status412PreconditionFailed, Type = typeof(ProblemDetails))]
    [ProducesResponseType(StatusCodes.Status428PreconditionRequired, Type = typeof(ProblemDetails))]
    public async Task<ActionResult<StoredFileResponse>> Confirm(Guid fileId, CancellationToken cancellationToken)
    {
        if (ReadPrecondition(out var precondition, out bool requiresExistence) is { } refusal)
        {
            return refusal;
        }

        var command = new ConfirmFileUploadCommand(fileId, precondition);
        var result = RequiringExistence(
            requiresExistence,
            await confirmFileUpload.ExecuteAsync(command, cancellationToken));

        return UpdatedOrProblem(StoredFileResponseMapping.ToFileResponse(result));
    }

    /// <summary>Deletes a file and lets its bytes be reclaimed.</summary>
    [HttpDelete("{fileId:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status400BadRequest, Type = typeof(ValidationProblemDetails))]
    [ProducesResponseType(StatusCodes.Status404NotFound, Type = typeof(ProblemDetails))]
    [ProducesResponseType(StatusCodes.Status409Conflict, Type = typeof(ProblemDetails))]
    [ProducesResponseType(StatusCodes.Status412PreconditionFailed, Type = typeof(ProblemDetails))]
    [ProducesResponseType(StatusCodes.Status428PreconditionRequired, Type = typeof(ProblemDetails))]
    public async Task<ActionResult> Delete(Guid fileId, CancellationToken cancellationToken)
    {
        if (ReadPrecondition(out var precondition, out bool requiresExistence) is { } refusal)
        {
            return refusal;
        }

        var command = new DeleteStoredFileCommand(fileId, precondition);
        var result = RequiringExistence(
            requiresExistence,
            await deleteStoredFile.ExecuteAsync(command, cancellationToken));

        return NoContentOrProblem(result);
    }

    private sealed class ApiUploadBodyLimitAttribute : RequestBodyLimitAttribute
    {
        private const long _framingBytes = 16 * 1024;

        public override long MaxRequestBodyBytes(IServiceProvider services) =>
            services.GetRequiredService<IOptions<FilesOptions>>().Value.ApiUploadMaxBytes + _framingBytes;
    }
}
