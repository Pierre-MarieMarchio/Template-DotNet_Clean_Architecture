using AppTemplate.Api.Common.Caching;
using AppTemplate.Api.Common.Contracts;
using AppTemplate.Api.Common.Controllers;
using AppTemplate.Api.Common.Errors;
using AppTemplate.Api.Common.Idempotency;
using AppTemplate.Api.Features.Files.Contracts.Requests;
using AppTemplate.Api.Features.Files.Contracts.Responses;
using AppTemplate.Api.Features.Files.Mapping;
using AppTemplate.Application.Features.Files.UseCases.Commands.ConfirmFileUpload;
using AppTemplate.Application.Features.Files.UseCases.Commands.DeleteStoredFile;
using AppTemplate.Application.Features.Files.UseCases.Commands.RegisterFile;
using AppTemplate.Application.Features.Files.UseCases.Queries.GetStoredFile;
using AppTemplate.Application.Features.Files.UseCases.Queries.GetStoredFiles;
using AppTemplate.Application.Features.Files.UseCases.Queries.IssueFileDownload;
using Microsoft.AspNetCore.Mvc;

namespace AppTemplate.Api.Features.Files.Controllers;

/// <summary>
/// The stored file aggregate's HTTP surface. <b>No byte of any file passes through it</b>, in either
/// direction.
/// </summary>
/// <remarks>
/// Authorisation is not declared here: <c>Program.cs</c> installs a fallback policy requiring an
/// authenticated user, and nothing on this controller opts out.
/// <para>
/// <b>Depositing is two requests, and that is forced rather than chosen.</b>
/// <c>RequestLimitsOptions.MaxRequestBodyBytes</c> caps an inbound body at 64 KiB (validated ceiling
/// 30 MiB), and <see cref="IdempotencyFilter"/> buffers and SHA-256s the whole body of every
/// <c>POST</c> before a handler sees it. So <see cref="Register"/> reserves a place and hands back a
/// signed grant, the client deposits the bytes straight onto the object store, and
/// <see cref="Confirm"/> makes the file readable. Every body on this controller is metadata — a
/// name, a media type, a length, a digest — a few hundred characters whatever the file weighs, which
/// is why no action here needs a request-size limit of its own and why none carries one. Raising the
/// cap for this feature would be the mistake, not the omission.
/// </para>
/// <para>
/// <b>Reading is a redirect, never a body.</b> <see cref="GetContent"/> answers <c>302</c> with a
/// short-lived signed URL and the content travels between the client and the store. Serving or
/// transforming bytes here — resizing an image on the way out, say — would put unbounded CPU inside
/// a process whose job is to answer in milliseconds, which is a denial of service a caller gets to
/// choose the cost of.
/// </para>
/// <para>
/// <b>Conditional requests.</b> A file's content never changes, but the resource does: the version
/// moves when a deposit is confirmed. So the reads that name one file publish that version as a
/// strong <c>ETag</c>, and the two writes that name one file honour <c>If-Match</c> — see each
/// action for what the condition buys. Registration takes none: there is no resource yet to have a
/// version, and two callers registering files are not competing for one.
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

    /// <summary>Redirects to a short-lived signed URL for the file's content.</summary>
    /// <remarks>
    /// <b>302, and no body.</b> Minting a signature changes nothing anywhere, so this is a
    /// <c>GET</c>; and a 302 is what lets an <c>&lt;img&gt;</c>, a download manager or a
    /// <c>curl -L</c> follow it with no client code at all. 307 would promise the method is
    /// preserved, which nothing here needs, and 301/308 would tell every cache the mapping is
    /// permanent when the target is worthless in minutes.
    /// <para>
    /// <b><c>[NoStore]</c>, and it is load-bearing.</b> The <c>Location</c> header is a bearer
    /// credential: whoever holds it reads the file, with no identity attached. The default for a read
    /// on this API is <c>private, no-cache</c>, which still permits a client to store the response —
    /// and a stored redirect outlives the grant it names, so a client replaying it gets a signature
    /// the store has already stopped honouring. <c>no-store</c> is the one directive that says the
    /// answer is good once.
    /// </para>
    /// <para>
    /// <b>The URL names the store, the bucket and the object key, and that is accepted.</b> It is
    /// disclosed only to the file's owner, who may read the bytes anyway; the key carries 128 bits
    /// from a cryptographic generator and is derived from nothing, so holding one says nothing about
    /// any other file's; and a bucket whose safety depends on its name being unknown is misconfigured
    /// with or without this endpoint. What the disclosure does forbid is treating the URL as
    /// ordinary: it is never logged, never stored and never handed to anyone but the caller who
    /// earned it.
    /// </para>
    /// <para>
    /// No <c>ETag</c>: this response represents a grant, not the file. Two calls a second apart
    /// answer with different signatures and different expiries, so a validator over it would name
    /// something that is never the same twice.
    /// </para>
    /// </remarks>
    [HttpGet("{fileId:guid}/content")]
    [NoStore]
    [ProducesResponseType(StatusCodes.Status302Found)]
    [ProducesResponseType(StatusCodes.Status400BadRequest, Type = typeof(ValidationProblemDetails))]
    [ProducesResponseType(StatusCodes.Status404NotFound, Type = typeof(ProblemDetails))]
    [ProducesResponseType(StatusCodes.Status409Conflict, Type = typeof(ProblemDetails))]
    public async Task<ActionResult> GetContent(Guid fileId, CancellationToken cancellationToken)
    {
        var query = new IssueFileDownloadQuery(fileId);
        var result = await issueFileDownload.ExecuteAsync(query, cancellationToken);

        // Mapped here rather than through a base helper: ApiControllerBase turns a Result into a
        // representation, and this action has none to return. ErrorMapping is the single place an
        // Error becomes a response either way, so the failure path is the same one every other action
        // takes.
        return result.IsFailure
            ? result.Error!.ToActionResult(HttpContext)
            : Redirect(result.Value.Url);
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
    /// 409 covers both refusals worth naming: the caller's quota (<c>storedFile.quotaExceeded</c>),
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
}
