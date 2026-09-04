using AppTemplate.Application.Common.Results;

namespace AppTemplate.Application.Features.Files.Errors;

public static class StoredFileErrors
{
    /// <summary>
    /// Also returned when the file belongs to somebody else: distinguishing the two would let a
    /// caller enumerate other users' file ids by comparing 403 against 404.
    /// </summary>
    public static Error FileNotFound(Guid storedFileId) => Error.NotFound(
        "storedFile.notFound",
        $"No file with id '{storedFileId}' was found.");

    /// <summary>
    /// Confirmation was asked for and the store holds nothing under the file's key. Reported rather
    /// than retried: the client is the only party that knows whether it ever sent the bytes, and the
    /// registration is removed on its own schedule if it never does.
    /// </summary>
    public static Error DepositMissing(Guid storedFileId) => Error.Conflict(
        "storedFile.depositMissing",
        $"No content has been deposited for file '{storedFileId}'.");

    /// <summary>
    /// A file whose content is not cleared for serving has no bytes to hand a right to — either
    /// because nothing was deposited, or because what was deposited has not been examined yet.
    /// <para>
    /// Both cases share one code on purpose: they are the same answer to the caller — not yet, ask
    /// again — and the state the caller can read on the file itself is where the difference is
    /// published, rather than in a code a client would have to switch on.
    /// </para>
    /// </summary>
    public static Error FileNotAvailable(Guid storedFileId) => Error.Conflict(
        "storedFile.notAvailable",
        $"File '{storedFileId}' has no content cleared for download yet.");

    /// <summary>
    /// A file whose content was examined and refused. <b>Its own code, unlike the two states above
    /// it</b>: this one will never become available however long the caller waits, and a client that
    /// could not tell it from "not yet" would poll for ever.
    /// <para>
    /// The message says that the content was refused and does not say what was found in it. Which
    /// detector fired is an operator's fact — see the inspection pass, which logs it — and returning
    /// it here would let an uploader adjust a payload until it passes.
    /// </para>
    /// </summary>
    public static Error FileQuarantined(Guid storedFileId) => Error.Conflict(
        "storedFile.quarantined",
        $"The content deposited for file '{storedFileId}' was refused and cannot be downloaded.");

    /// <summary>
    /// <paramref name="message"/> describes the caller's own allowance and never the store's
    /// capacity or anyone else's usage, so it is safe to return verbatim.
    /// </summary>
    public static Error QuotaExceeded(string message) => Error.Conflict(
        "storedFile.quotaExceeded",
        message);
}
