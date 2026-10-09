using AppTemplate.Application.Core.Common.Results;

namespace AppTemplate.Application.Features.Files.Errors;

/// <summary>Errors of the Files feature.</summary>
public static class StoredFileErrors
{
    /// <summary>
    /// Also returned for another user's file: telling 403 from 404 would let a caller enumerate file ids.
    /// </summary>
    public static Error FileNotFound(Guid storedFileId) => Error.NotFound(
        "storedFile.notFound",
        $"No file with id '{storedFileId}' was found.");

    /// <summary>
    /// Confirmation was asked and the store holds nothing under the file's key. Reported, not retried: only the
    /// client knows whether it sent the bytes.
    /// </summary>
    public static Error DepositMissing(Guid storedFileId) => Error.Conflict(
        "storedFile.depositMissing",
        $"No content has been deposited for file '{storedFileId}'.");

    /// <summary>
    /// The file has no content cleared for serving: nothing deposited, or not examined yet. One code for both, since
    /// the caller's answer is the same (not yet); the file's state tells them apart.
    /// </summary>
    public static Error FileNotAvailable(Guid storedFileId) => Error.Conflict(
        "storedFile.notAvailable",
        $"File '{storedFileId}' has no content cleared for download yet.");

    /// <summary>
    /// The content was examined and refused. Its own code, because it never becomes available and a client that could
    /// not tell it from "not yet" would poll for ever. The message does not say what was found: that is the
    /// operator's log, and revealing it would let an uploader tune a payload until it passes.
    /// </summary>
    public static Error FileQuarantined(Guid storedFileId) => Error.Conflict(
        "storedFile.quarantined",
        $"The content deposited for file '{storedFileId}' was refused and cannot be downloaded.");

    /// <summary>
    /// Direct-upload registration on a store that cannot sign URLs: the deployment's choice, so no retry changes it.
    /// </summary>
    public static Error DirectUploadUnavailable() => Error.Conflict(
        "storedFile.directUploadUnavailable",
        "This deployment's file storage does not accept direct uploads; upload the file through the API instead.");

    /// <summary>
    /// Direct-upload registration in a category configured for API uploads only. Same code as
    /// <see cref="DirectUploadUnavailable"/>, same answer for the caller.
    /// </summary>
    public static Error DirectUploadNotAllowed(string category) => Error.Conflict(
        "storedFile.directUploadUnavailable",
        $"Category '{category}' does not accept direct uploads; upload the file through the API instead.");

    /// <summary>An API upload in a category configured for direct uploads only.</summary>
    public static Error ApiUploadNotAllowed(string category) => Error.Conflict(
        "storedFile.apiUploadUnavailable",
        $"Category '{category}' does not accept uploads through the API; register the file and deposit it directly instead.");

    /// <summary>
    /// <paramref name="message"/> describes only the caller's own allowance, so it is safe to return verbatim.
    /// </summary>
    public static Error QuotaExceeded(string message) => Error.Conflict(
        "storedFile.quotaExceeded",
        message);

    /// <summary>
    /// Another registration of the caller in the same strict category held the quota lock past the wait limit.
    /// Nothing was written; retry.
    /// </summary>
    public static Error QuotaBusy(string category) => Error.Conflict(
        "storedFile.quotaBusy",
        $"Another upload of yours in category '{category}' is being registered. Try again in a moment.");
}
