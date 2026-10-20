using AppTemplate.Application.Features.Files.Ports.FileContentScanner;
using AppTemplate.Domain.Features.Files.Model.ValueObjects;

namespace AppTemplate.Application.Features.Files.Model.Policies;

public static class StoredFileContentPolicy
{
    public const int MaxHeadBytes = 1024;

    public static ContentDecision Decide(
        DeclaredMediaType declared,
        FileScanOutcome? scan,
        ReadOnlySpan<byte> head,
        bool verifySignature)
    {
        ArgumentNullException.ThrowIfNull(declared);

        if (scan?.Status == FileScanStatus.Unavailable)
        {
            return ContentDecision.Retry;
        }

        if (scan?.Status is FileScanStatus.Infected or FileScanStatus.NotInspectable)
        {
            return ContentDecision.Quarantine;
        }

        if (MediaTypeSignatures.IsScriptContainer(head) || MediaTypeSignatures.BeginsAsMarkup(head))
        {
            return ContentDecision.Quarantine;
        }

        return verifySignature ? DecideFromSignature(declared, head) : ContentDecision.Release;
    }

    private static ContentDecision DecideFromSignature(DeclaredMediaType declared, ReadOnlySpan<byte> head)
    {
        if (MediaTypeSignatures.DetectedMediaTypeOf(head) is { } detected)
        {
            return string.Equals(detected, declared.Value, StringComparison.Ordinal)
                ? ContentDecision.Release
                : ContentDecision.Quarantine;
        }

        return MediaTypeSignatures.IsRecognisable(declared.Value)
            ? ContentDecision.Quarantine
            : ContentDecision.Release;
    }
}
