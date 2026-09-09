using AppTemplate.Application.Features.Files.Ports.FileContentInspector;
using AppTemplate.Domain.Features.Files.ValueObjects;

namespace AppTemplate.Application.Features.Files.Policies;

/// <summary>
/// What an inspection's findings mean for the file. This is the decision half of the inspection, and
/// it is here rather than behind the port so that it can be read, argued with and tested without a
/// scanner, a bucket or a socket.
/// <para>
/// <b>The type check needs nothing configured and always runs</b>; the malware verdict is a
/// capability a deployment adds. A file in a deployment with no scanner is still refused for being
/// an SVG dressed as a PNG; it is not refused for carrying a virus, because nothing looked.
/// </para>
/// </summary>
public static class StoredFileContentPolicy
{
    /// <summary>
    /// Whether a file whose content produced <paramref name="inspection"/> may be released, must be
    /// refused, or has to be asked about again.
    /// </summary>
    /// <param name="declared">What the client said the file is. Already normalised — lower-cased,
    /// no parameters — which is what makes the comparison below an ordinal string comparison rather
    /// than a media-type parser.</param>
    public static ContentDecision Decide(DeclaredMediaType declared, ContentInspectionOutcome inspection)
    {
        ArgumentNullException.ThrowIfNull(declared);
        ArgumentNullException.ThrowIfNull(inspection);

        // Before anything else, so an outage is never read as a pass.
        if (inspection.Status == ContentInspectionStatus.Unavailable)
        {
            return ContentDecision.Retry;
        }

        if (inspection.Status == ContentInspectionStatus.Infected)
        {
            return ContentDecision.Quarantine;
        }

        // A permanent condition, so retrying would park the file for ever and releasing it would
        // make "upload something larger than the scanner accepts" the way to skip the scan.
        if (inspection.Status == ContentInspectionStatus.NotInspectable)
        {
            return ContentDecision.Quarantine;
        }

        return DecideFromContent(declared, inspection.Head.Span);
    }

    /// <summary>
    /// The three questions the leading bytes can answer, in the order that makes each one's answer
    /// final.
    /// </summary>
    private static ContentDecision DecideFromContent(DeclaredMediaType declared, ReadOnlySpan<byte> head)
    {
        // First, and regardless of what was declared. Nothing here sanitises an SVG and the download
        // path cannot make one safe: it hands out a signed URL to a store this application does not
        // configure. Refusing the format is the only rule this layer can enforce — SECURITY.md names
        // it as the likeliest gap, and a project that needs SVG owes a sanitiser with it.
        //
        // Two checks: the signature search reads a prefix, so markup pushed past it goes unseen,
        // while the start of the document is what an author cannot push.
        if (MediaTypeSignatures.IsScriptContainer(head) || MediaTypeSignatures.BeginsAsMarkup(head))
        {
            return ContentDecision.Quarantine;
        }

        // Second: the content named itself. Exact, so an unrecognised spelling — 'image/jpg' for a
        // JPEG — is refused rather than guessed at; a table of aliases is a second place to disagree.
        if (MediaTypeSignatures.DetectedMediaTypeOf(head) is { } detected)
        {
            return string.Equals(detected, declared.Value, StringComparison.Ordinal)
                ? ContentDecision.Release
                : ContentDecision.Quarantine;
        }

        // Third: the content named nothing, so the declaration is checked in the other direction. A
        // file claiming to be a PNG has to start like one. Without this, uploading a format the
        // table has no signature for would evade the whole check.
        //
        // The types with no signature — CSV, JSON, plain text, every ZIP-based document format —
        // reach here and are released. That is the limit of what leading bytes decide, and why the
        // markup rule above exists.
        return MediaTypeSignatures.IsRecognisable(declared.Value)
            ? ContentDecision.Quarantine
            : ContentDecision.Release;
    }
}
