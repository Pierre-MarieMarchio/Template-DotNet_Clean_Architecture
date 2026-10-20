using AppTemplate.Application.Core.Common.Results;

namespace AppTemplate.Application.Features.Files.Model.Errors;

public static class StoredFileErrors
{
    public static Error FileNotFound(Guid storedFileId) => Error.NotFound(
        "storedFile.notFound",
        $"No file with id '{storedFileId}' was found.");

    public static Error DepositMissing(Guid storedFileId) => Error.Conflict(
        "storedFile.depositMissing",
        $"No content has been deposited for file '{storedFileId}'.");

    public static Error FileNotAvailable(Guid storedFileId) => Error.Conflict(
        "storedFile.notAvailable",
        $"File '{storedFileId}' has no content cleared for download yet.");

    public static Error FileQuarantined(Guid storedFileId) => Error.Conflict(
        "storedFile.quarantined",
        $"The content deposited for file '{storedFileId}' was refused and cannot be downloaded.");

    public static Error DirectUploadUnavailable() => Error.Conflict(
        "storedFile.directUploadUnavailable",
        "This deployment's file storage does not accept direct uploads; upload the file through the API instead.");

    public static Error DirectUploadNotAllowed(string category) => Error.Conflict(
        "storedFile.directUploadUnavailable",
        $"Category '{category}' does not accept direct uploads; upload the file through the API instead.");

    public static Error ApiUploadNotAllowed(string category) => Error.Conflict(
        "storedFile.apiUploadUnavailable",
        $"Category '{category}' does not accept uploads through the API; register the file and deposit it directly instead.");

    public static Error QuotaExceeded(string message) => Error.Conflict(
        "storedFile.quotaExceeded",
        message);

    public static Error QuotaBusy(string category) => Error.Conflict(
        "storedFile.quotaBusy",
        $"Another upload of yours in category '{category}' is being registered. Try again in a moment.");
}
