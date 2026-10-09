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
/// <b>Two ways to deposit, one state machine.</b> Directly: <see cref="Register"/> reserves a place
/// and hands back a signed grant, the client deposits the bytes on the object store, and
/// <see cref="Confirm"/> records the deposit. Through the API: <see cref="Upload"/> carries the bytes
/// in a multipart body, the only action that raises the request-size limit. Both leave the file
/// <c>deposited</c> for the background inspection.
/// <para>
/// <b>Reading is a redirect when the store can sign, and a stream when it cannot.</b>
/// <see cref="GetContent"/> answers <c>302</c> with a short-lived signed URL, or — with the local
/// file system store — <c>200</c> with the bytes.
/// </para>
/// <para>
/// <b>Conditional requests.</b> The version moves when a deposit is confirmed. The reads naming one
/// file publish it as a strong <c>ETag</c> and the two writes naming one file honour
/// <c>If-Match</c>. Registration takes none — there is no resource yet to have a version.
/// </para>
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
    /// Two paging modes: <c>offset</c> (the default), addressed by <c>page</c>/<c>pageSize</c> and
    /// answering a <c>totalCount</c>; and <c>cursor</c>, addressed by an opaque token minted by the
    /// previous page's <c>nextCursor</c>. <c>sort</c> is a comma-separated list of whitelisted fields
    /// (<c>name</c>, <c>registeredAt</c>, <c>availableAt</c>), each optionally suffixed
    /// <c>:asc</c>/<c>:desc</c>; cursor mode allows one, and <c>availableAt</c> is offset-only
    /// because its column is nullable. <c>search</c> matches the file name; <c>state</c> narrows to
    /// <c>pending</c> or <c>available</c>.
    /// <para>
    /// No <c>ETag</c>: a page of files is not one aggregate, so there is no single version that
    /// describes it and no write for <c>If-Match</c> to guard.
    /// </para>
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
    /// This is the only endpoint that publishes a file's version, so it is also how a client that has
    /// just registered obtains the validator <see cref="Confirm"/> and <see cref="Delete"/> compare
    /// against — <see cref="Register"/> answers with a grant, not a representation, and has no
    /// version to publish.
    /// <para>
    /// A file that belongs to somebody else answers exactly as an absent one does. That is decided by
    /// the read port, which filters by owner inside the query rather than after it; nothing here
    /// re-derives the distinction, because a 403 next to a 404 is how an id becomes a probe.
    /// </para>
    /// <para>
    /// <b>Not <c>GetById</c>.</b> A route name is global to the application, not scoped to its
    /// controller, and <c>TodoListsController</c> already owns that one: two actions sharing a name
    /// under different templates make MVC throw while it is building its action descriptors, so the
    /// whole host fails to start rather than one endpoint misbehaving. Renaming this back is not a
    /// tidy-up.
    /// </para>
    /// </remarks>
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
    /// <b>302</b> when the store signs URLs: an <c>&lt;img&gt;</c>, a download manager or
    /// <c>curl -L</c> follows it with no client code. 301/308 would tell caches the mapping is
    /// permanent when the target is worthless in minutes.
    /// <para>
    /// <b>200</b> with the bytes when it cannot (the local file system store): as an attachment
    /// under the file's name, with the declared media type and <c>nosniff</c>, so a browser never
    /// renders what the uploader chose.
    /// </para>
    /// <para>
    /// <b><c>[NoStore]</c>, and it is load-bearing.</b> The <c>Location</c> is a bearer credential,
    /// and a stored redirect outlives the grant it names. The URL is disclosed only to a caller who may
    /// read the file — its owner, or any signed-in user for an <c>Authenticated</c> category —
    /// and must never be logged or stored.
    /// </para>
    /// <para>No <c>ETag</c>: a grant is never the same twice.</para>
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

        // Mapped here rather than through a base helper: there is no representation to return.
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
    /// <b>Idempotent, and this is the endpoint that needs it.</b> Registering is unaddressed
    /// creation: the client cannot name what it is about to create, so a retried request is
    /// indistinguishable from a second one, and replaying it mints a second file, a second object key
    /// and a second signed grant — burning one of the caller's twenty pending slots for a file it
    /// never asked for twice. Send an <c>Idempotency-Key</c> header and the retry gets the first
    /// response back, grant and all, under <c>Idempotency-Replayed: true</c>. <see cref="Confirm"/>
    /// deliberately has no such attribute; its own remarks say why.
    /// <para>
    /// <b>The filter hashes the whole request body, and that is harmless here.</b> On a feature about
    /// files that reads like a trap, so it is worth saying plainly: the body this filter buffers and
    /// digests is <see cref="RegisterFileRequest"/> — a name, a media type, a length and a digest.
    /// The file's own bytes never enter this pipeline, so there is nothing large to buffer and
    /// nothing expensive to hash.
    /// </para>
    /// <para>
    /// <b><c>[NoStore]</c>:</b> the body carries a signed write URL, which is a credential on the
    /// same footing as a token — see <c>NoStoreAttribute</c>. A <c>POST</c> gets no
    /// <c>Cache-Control</c> from this API's default at all, so without this the response would state
    /// nothing about a credential it carries.
    /// </para>
    /// <para>
    /// 400 refuses a category the deployment does not declare, a media type it does not allow and a
    /// size above its maximum. 409 covers the refusals worth naming: a store that cannot sign or a
    /// category without direct uploads (<c>storedFile.directUploadUnavailable</c>), the caller's quota
    /// in that category (<c>storedFile.quotaExceeded</c>),
    /// and a value the domain refuses that this layer's validation cannot state without restating the
    /// domain's rules — a reserved device name, a wildcard media type, a checksum of the right length
    /// that is not hexadecimal.
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

        // The failure is answered on its own line because Location has to name the file that was
        // created, and the only overload that takes route values as a function needs a versioned
        // result — which registration does not produce. Reading Value on a failure throws.
        if (result.IsFailure)
        {
            return result.Error!.ToActionResult(HttpContext);
        }

        return CreatedOrProblem(result, nameof(GetFile), new { fileId = result.Value.Id });
    }

    /// <summary>Uploads a file through the API: registers it, stores its bytes and confirms the deposit.</summary>
    /// <remarks>
    /// <c>multipart/form-data</c> with <c>category</c>, exactly one <c>file</c> part, whose
    /// <c>Content-Type</c> is the declared media type, and an optional <c>name</c> that defaults to
    /// the part's file name. The body may be <c>Files:ApiUploadMaxBytes</c> plus 16 KiB of framing;
    /// beyond that, 413. The file comes back <c>deposited</c>, as after <see cref="Confirm"/>.
    /// <para>
    /// <b>Not <c>[Idempotent]</c>:</b> the filter would buffer and hash the whole file. A retried
    /// request uploads a second file; a client that cannot tell whether the first arrived lists its
    /// files. <b><c>[NoStore]</c>:</b> a <c>POST</c> gets no <c>Cache-Control</c> from this API's
    /// default, and this response describes a private file.
    /// </para>
    /// <para>
    /// 400 for a missing or second file part, an unknown category, a media type it does not allow or
    /// a file above its maximum or above the API ceiling. 409 for a category without API uploads
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

    /// <summary>Confirms that the bytes have been deposited, making the file readable.</summary>
    /// <remarks>
    /// A named operation rather than a <c>PUT</c> of a status field: what happens here is the store
    /// being asked what it actually holds, and the aggregate refusing to move unless that matches
    /// what was declared. The request carries no body for the same reason — a client repeating its
    /// own declaration would confirm only that it can repeat itself.
    /// <para>
    /// <b>Not <c>[Idempotent]</c>, on purpose.</b> This <c>POST</c> names the file it acts on, so a
    /// retry cannot create a second anything; and the transition is one-way, so the second call meets
    /// a file that is no longer pending and is refused with a 409 rather than doing the work twice.
    /// The identity of the resource is already carrying what an idempotency key would buy, and
    /// claiming a key would add a store round trip plus an <c>idempotency.inProgress</c> refusal to a
    /// request that is safe to repeat.
    /// </para>
    /// <para>
    /// <b>Conditional, and the condition is not decoration.</b> <c>If-Match</c> with the entity tag
    /// from <see cref="GetFile"/> says "confirm the registration I read, not whatever is there now".
    /// <c>If-Match: *</c> is the more useful form here and is the reason this endpoint is conditional
    /// at all: a registration that was never deposited against is removed by the abandonment sweep,
    /// so a client resuming after a long upload is asking a real question when it asks whether its
    /// registration still exists — and gets 412 rather than a 404 it might read as "wrong id".
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
    /// <b>A file's content is immutable, and a conditional delete is still worth having</b> — the two
    /// are not the same statement. The version tracks the file's existence and state, not its bytes,
    /// and it moves exactly once: when a deposit is confirmed. So the lost update this guards against
    /// is a real one: a client that read a file as <c>pending</c> and decided to clean up an upload
    /// it thought had failed will, unconditionally, also delete the content that arrived in between.
    /// <c>If-Match</c> on the version it read turns that into a 412.
    /// <para>
    /// 204 with no body: there is no soft delete here and no deletion instant, so there is nothing
    /// left to describe.
    /// </para>
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
