using AppTemplate.Application.Features.Files.Ports.FileContentScanner;
using AppTemplate.Domain.Features.Files.ValueObjects;

namespace AppTemplate.Application.Features.Files.Policies;

/// <summary>
/// What a file's leading bytes and its scan mean for the file: release, refuse, or ask again.
/// <para>
/// The type check needs nothing configured and always runs; the malware verdict is a capability a
/// deployment adds. With no scanner, an SVG dressed as a PNG is still refused.
/// </para>
/// </summary>
public static class StoredFileContentPolicy
{
    /// <summary>
    /// How many leading bytes the decision reads. Every known signature sits in the first sixteen;
    /// the kibibyte is for markup, whose <c>&lt;svg</c> may follow a declaration, a doctype and comments.
    /// </summary>
    public const int MaxHeadBytes = 1024;

    /// <summary>
    /// Whether a file whose content starts with <paramref name="head"/> and scanned as
    /// <paramref name="scan"/> may be released, must be refused, or has to be asked about again.
    /// </summary>
    /// <param name="declared">What the client said the file is, already normalised, so the
    /// comparison below is ordinal.</param>
    /// <param name="head">At most <see cref="MaxHeadBytes"/> leading bytes of the content.</param>
    public static ContentDecision Decide(DeclaredMediaType declared, FileScanOutcome scan, ReadOnlySpan<byte> head)
    {
        ArgumentNullException.ThrowIfNull(declared);
        ArgumentNullException.ThrowIfNull(scan);

        // Before anything else, so an outage is never read as a pass.
        if (scan.Status == FileScanStatus.Unavailable)
        {
            return ContentDecision.Retry;
        }

        // NotInspectable is permanent: retrying would park the file for ever, and releasing it would
        // make "upload something larger than the scanner accepts" the way to skip the scan.
        if (scan.Status is FileScanStatus.Infected or FileScanStatus.NotInspectable)
        {
            return ContentDecision.Quarantine;
        }

        return DecideFromContent(declared, head);
    }

    private static ContentDecision DecideFromContent(DeclaredMediaType declared, ReadOnlySpan<byte> head)
    {
        // Script containers first, whatever was declared: nothing here sanitises an SVG (SECURITY.md).
        // Two checks, because markup can be pushed past the prefix but not away from the start.
        if (MediaTypeSignatures.IsScriptContainer(head) || MediaTypeSignatures.BeginsAsMarkup(head))
        {
            return ContentDecision.Quarantine;
        }

        // The content named itself: exact match, so 'image/jpg' for a JPEG is refused, not guessed at.
        if (MediaTypeSignatures.DetectedMediaTypeOf(head) is { } detected)
        {
            return string.Equals(detected, declared.Value, StringComparison.Ordinal)
                ? ContentDecision.Release
                : ContentDecision.Quarantine;
        }

        // The content named nothing: a declared type that has a signature must show it. Types with
        // none (CSV, JSON, text, ZIP-based documents) are released — the limit of what a head decides.
        return MediaTypeSignatures.IsRecognisable(declared.Value)
            ? ContentDecision.Quarantine
            : ContentDecision.Release;
    }
}
