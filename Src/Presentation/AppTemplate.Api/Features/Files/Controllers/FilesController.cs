using AppTemplate.Api.Common.Caching;
using AppTemplate.Api.Common.Contracts;
using AppTemplate.Api.Common.Controllers;
using AppTemplate.Api.Common.Errors;
using AppTemplate.Api.Common.Hosting;
using AppTemplate.Api.Common.Idempotency;
using AppTemplate.Api.Features.Files.Contracts.Requests;
using AppTemplate.Api.Features.Files.Contracts.Responses;
using AppTemplate.Api.Features.Files.Mapping;
using AppTemplate.Application.Core.Common.Validation;
using AppTemplate.Application.Features.Files.Options;
using AppTemplate.Application.Features.Files.Policies;
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

/// <summary>
/// The stored file aggregate's HTTP surface.
/// </summary>
/// <remarks>
/// A file is deposited either directly (<see cref="Register"/>, a signed upload to the store, then
/// <see cref="Confirm"/>) or through the API (<see cref="Upload"/>, multipart); both leave it
/// <c>deposited</c> for the background inspection. Reads naming one file publish a strong
/// <c>ETag</c> and the writes naming one honour <c>If-Match</c>.
/// </remarks>
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
    /// <remarks>
    /// <c>offset</c> paging (default; <c>page</c>/<c>pageSize</c>, answers <c>totalCount</c>) or
    /// <c>cursor</c> paging (opaque <c>nextCursor</c> of the previous page). <c>sort</c> lists whitelisted
    /// fields (<c>name</c>, <c>registeredAt</c>, <c>availableAt</c>) with an optional <c>:asc</c>/<c>:desc</c>;
    /// cursor mode allows one, and <c>availableAt</c> is offset-only because its column is nullable.
    /// <c>search</c> matches the file name. No <c>ETag</c>: a page is not one aggregate.
    /// </remarks>
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

    /// <summary>Gets one file's metadata, and the <c>ETag</c> the writes below are conditioned on.</summary>
    /// <remarks>
    /// The only endpoint that publishes a file's version. A file owned by somebody else answers as an
    /// absent one: the read port filters by owner, so a 403 never sits next to a 404.
    /// </remarks>
    // Not named GetById: route names are global and TodoListsController owns it, which makes MVC
    // throw at startup.
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
    /// <remarks>
    /// <b>302</b> when the store signs URLs; <b>200</b> with the bytes, as an attachment under the file's
    /// name with the declared media type and <c>nosniff</c>, when it cannot (the file system store).
    /// <c>[NoStore]</c> is load-bearing: the <c>Location</c> is a bearer credential that a cached
    /// redirect would outlive. No <c>ETag</c>: a grant is never the same twice.
    /// </remarks>
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

        // No representation to return, so the failure is mapped here.
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
    /// <remarks>
    /// Idempotent: registering is unaddressed creation, so a retry would otherwise mint a second file,
    /// key and grant. Send <c>Idempotency-Key</c>; a retry gets the first response back under
    /// <c>Idempotency-Replayed: true</c>. The filter buffers only <see cref="RegisterFileRequest"/>,
    /// never the file's bytes. <c>[NoStore]</c>: the signed write URL is a credential.
    /// <para>
    /// 400 for an undeclared category, a media type it does not allow or a size above its maximum. 409
    /// for a store or category without direct upload (<c>storedFile.directUploadUnavailable</c>), the
    /// quota (<c>storedFile.quotaExceeded</c>) and a value only the domain can refuse (reserved device
    /// name, wildcard media type, non-hexadecimal checksum).
    /// </para>
    /// </remarks>
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

        // Location must name the created file, and Value cannot be read on a failure.
        if (result.IsFailure)
        {
            return result.Error!.ToActionResult(HttpContext);
        }

        return CreatedOrProblem(result, nameof(GetFile), new { fileId = result.Value.Id });
    }

    /// <summary>Uploads a file through the API: registers it, stores its bytes and confirms the deposit.</summary>
    /// <remarks>
    /// <c>multipart/form-data</c> with <c>category</c>, exactly one <c>file</c> part whose
    /// <c>Content-Type</c> is the declared media type, and an optional <c>name</c> (default: the part's
    /// file name). The body is limited to <c>Files:ApiUploadMaxBytes</c> plus 16 KiB of framing, else
    /// 413. The file comes back <c>deposited</c>. Not <c>[Idempotent]</c> (the filter would buffer the
    /// file): a retry uploads a second file. <c>[NoStore]</c>: a <c>POST</c> has no default
    /// <c>Cache-Control</c>.
    /// <para>
    /// 400 for a missing or second file part, an unknown category, a media type it does not allow or a
    /// file above its maximum or the API ceiling. 409 for a category without API uploads
    /// (<c>storedFile.apiUploadUnavailable</c>), the quota (<c>storedFile.quotaExceeded</c>,
    /// <c>storedFile.quotaBusy</c>) and a value the domain refuses.
    /// </para>
    /// </remarks>
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
    /// <remarks>
    /// A named operation with no body: the store is asked what it holds and the aggregate refuses unless
    /// it matches the declaration. Not <c>[Idempotent]</c>: the route names the file and the transition
    /// is one-way, so a retry meets a non-pending file and gets 409.
    /// <para>
    /// Conditional: <c>If-Match: *</c> asks whether the registration still exists, since the abandonment
    /// sweep removes undeposited ones; a client resuming a long upload gets 412 rather than a 404 it
    /// might read as a wrong id.
    /// </para>
    /// </remarks>
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
    /// <remarks>
    /// The version moves once, when a deposit is confirmed, so <c>If-Match</c> stops a client that read
    /// the file as <c>pending</c> from deleting content that arrived since. 204 with no body: there is
    /// no soft delete.
    /// </remarks>
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

    /// <summary><c>Files:ApiUploadMaxBytes</c>, plus room for the multipart framing around the file.</summary>
    private sealed class ApiUploadBodyLimitAttribute : RequestBodyLimitAttribute
    {
        private const long _framingBytes = 16 * 1024;

        public override long MaxRequestBodyBytes(IServiceProvider services) =>
            services.GetRequiredService<IOptions<FilesOptions>>().Value.ApiUploadMaxBytes + _framingBytes;
    }
}
