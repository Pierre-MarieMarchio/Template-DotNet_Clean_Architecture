using AppTemplate.Application.Features.Files.Ports.FileContentInspector;
using AppTemplate.Domain.Features.Files.ValueObjects;

namespace AppTemplate.Application.Features.Files.Policies;

/// <summary>
/// What an inspection's findings mean for the file. This is the decision half of the inspection, and
/// it is here rather than behind the port so that it can be read, argued with and tested without a
/// scanner, a bucket or a socket.
/// <para>
/// <b>It closes the gap <c>SECURITY.md</c> names: "the declared media type is a claim, not a
/// fact".</b> The claim is now checked against the content, and a file whose bytes contradict it is
/// refused rather than served under a type it is not.
/// </para>
/// <para>
/// <b>The type check needs nothing configured and always runs</b>; the malware verdict is a
/// capability a deployment adds. That asymmetry is deliberate and it is what a template can honestly
/// promise: a signature table ships with the code and works in every deployment, and an antivirus
/// daemon is an operational dependency that some deployments will not have. A file in a deployment
/// with no scanner is still refused for being an SVG dressed as a PNG; it is not refused for
/// carrying a virus, because nothing looked.
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
    public static ContentVerdict Decide(DeclaredMediaType declared, ContentInspectionOutcome inspection)
    {
        ArgumentNullException.ThrowIfNull(declared);
        ArgumentNullException.ThrowIfNull(inspection);

        // Checked before anything else, so that an outage can never be read as a pass. Everything
        // below this line is a verdict about bytes somebody actually looked at.
        if (inspection.Status == ContentInspectionStatus.Unavailable)
        {
            return ContentVerdict.Retry;
        }

        if (inspection.Status == ContentInspectionStatus.Infected)
        {
            return ContentVerdict.Quarantine;
        }

        // Content no scanner will ever look at is refused rather than left waiting. The condition is
        // permanent — the object is past a limit the object cannot change — so retrying would park
        // the file for ever, and releasing it would make "upload something larger than the scanner
        // accepts" the way to skip the scan. Refusing is the only one of the three that neither
        // strands the file nor rewards the size.
        if (inspection.Status == ContentInspectionStatus.NotInspectable)
        {
            return ContentVerdict.Quarantine;
        }

        return DecideFromContent(declared, inspection.Head.Span);
    }

    /// <summary>
    /// The three questions the leading bytes can answer, in the order that makes each one's answer
    /// final.
    /// </summary>
    private static ContentVerdict DecideFromContent(DeclaredMediaType declared, ReadOnlySpan<byte> head)
    {
        // First, and regardless of what was declared. A script container is refused even when it is
        // declared honestly as one: nothing in this template sanitises an SVG, and the download path
        // cannot make one safe either — it hands out a signed URL to an origin this application does
        // not control, so whether the object is served as an attachment is a property of that
        // store's configuration rather than of any code here. Refusing the format is the only rule
        // this layer can actually enforce, and SECURITY.md names this as the gap most likely to be
        // exploited.
        //
        // A project that genuinely needs SVG changes this deliberately, and owes a sanitiser and a
        // serving path that cannot execute what it stores.
        // Two checks, because one of them is bounded and the other cannot be. The search reads a
        // prefix, so markup pushed past it is markup nothing sees; what an author cannot push is the
        // start of the document, and a well-formed SVG's first meaningful byte is '<' however much
        // comment sits between that and its root element. See MediaTypeSignatures.BeginsAsMarkup.
        if (MediaTypeSignatures.IsScriptContainer(head) || MediaTypeSignatures.BeginsAsMarkup(head))
        {
            return ContentVerdict.Quarantine;
        }

        // Second: the content named itself. Deliberately exact — a spelling this template does not
        // recognise, 'image/jpg' for a JPEG say, is refused rather than guessed at, because a table
        // of aliases is a second place for the two sides to disagree.
        if (MediaTypeSignatures.DetectedMediaTypeOf(head) is { } detected)
        {
            return string.Equals(detected, declared.Value, StringComparison.Ordinal)
                ? ContentVerdict.Release
                : ContentVerdict.Quarantine;
        }

        // Third: the content named nothing, so the declaration is checked in the other direction. A
        // file claiming to be a PNG has to start like a PNG; that it starts like nothing this table
        // knows is enough to say it is not one. Without this the whole check would be evadable by
        // uploading a format the table has no signature for.
        //
        // The types with no signature — CSV, JSON, plain text, and every ZIP-based document format,
        // for the reason the table gives — reach here and are released. That is the deliberate limit
        // of what leading bytes can decide, and it is why the rule above it exists: the one format
        // that is dangerous *because* it has no signature is recognised from its markup instead.
        return MediaTypeSignatures.IsRecognisable(declared.Value)
            ? ContentVerdict.Quarantine
            : ContentVerdict.Release;
    }
}
