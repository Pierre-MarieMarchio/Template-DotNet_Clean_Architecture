using AppTemplate.Application.Common.Results;
using AppTemplate.Application.Common.Validation;
using AppTemplate.Application.Features.Files.Errors;
using AppTemplate.Application.Features.Files.Ports.FileContentStore;
using AppTemplate.Application.Features.Files.Services;
using AppTemplate.Domain.Features.Files.ValueObjects;
using FluentValidation;

namespace AppTemplate.Application.Features.Files.UseCases.Queries.IssueFileDownload;

/// <summary>
/// Turns "this caller may read this file" into a short-lived signed URL. <b>The API never serves the
/// bytes</b>; it says where they are and for how long that answer is good for.
/// <para>
/// <b>The authorisation happens here and cannot happen anywhere else.</b> A signed URL is a bearer
/// right — whoever holds it reads the file, with no identity attached and nothing left to check —
/// so this is the last moment at which the question "whose file is this?" can be asked at all. It is
/// asked of the aggregate, through the same gate every command uses, because ownership is a domain
/// fact and not a property of the transport that happens to be carrying the request.
/// </para>
/// <para>
/// A file whose deposit was never confirmed is refused rather than pointed at: its key may hold
/// nothing, or a partial object, and a grant for either would hand the caller a URL that answers
/// with a broken file and no explanation.
/// </para>
/// <para>
/// <b>The gate asks for <see cref="StoredFileState.Available"/> by name, and that one word is what
/// buys the whole inspection.</b> It is a whitelist, not a list of states to exclude, so a state
/// added to the enum is refused here on the day it is added rather than on the day somebody
/// remembers to extend a predicate. That is why quarantine can be a state on this aggregate at all
/// while a deleted flag cannot: refusing by default costs nothing, and a soft delete would have
/// needed a predicate in every query instead.
/// </para>
/// </summary>
public sealed class IssueFileDownloadUseCase(
    IStoredFileAccess files,
    IFileContentStore content,
    IValidator<IssueFileDownloadQuery> validator) : IIssueFileDownloadUseCase
{
    /// <summary>
    /// Deliberately minutes. The URL is about to be put in a <c>Location</c> header, which means a
    /// browser history entry, a referrer, and every proxy log in between — and each of those is a
    /// copy of a credential nobody can revoke. Long enough to start the download, and worthless
    /// soon after.
    /// </summary>
    private static readonly TimeSpan _downloadWindow = TimeSpan.FromMinutes(5);

    public async Task<Result<IssuedDownloadGrant>> ExecuteAsync(
        IssueFileDownloadQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        var validation = await validator.EnsureValidAsync(query, cancellationToken);

        if (validation.IsFailure)
        {
            return validation.To<IssuedDownloadGrant>();
        }

        // No precondition: a read is not a write, and there is no lost update to guard against.
        var access = await files.LoadOwnedAsync(query.StoredFileId, precondition: null, cancellationToken);

        if (access.IsFailure)
        {
            return access.To<IssuedDownloadGrant>();
        }

        var storedFile = access.Value;

        if (storedFile.State != StoredFileState.Available)
        {
            // Two answers rather than one, because they differ in whether waiting helps: a refused
            // file will never become available, and a client told only "not yet" would poll until it
            // gave up. Neither answer says anything about the content beyond the fact of the
            // refusal.
            return Result.Failure<IssuedDownloadGrant>(
                storedFile.State == StoredFileState.Quarantined
                    ? StoredFileErrors.FileQuarantined(storedFile.Id)
                    : StoredFileErrors.FileNotAvailable(storedFile.Id));
        }

        return await content.CreateDownloadGrantAsync(
            storedFile.ObjectKey.Value,
            storedFile.Name.Value,
            storedFile.DeclaredMediaType.Value,
            _downloadWindow,
            cancellationToken);
    }
}
