namespace AppTemplate.Application.Features.Files.Ports.FileContentInspector;

/// <summary>
/// What an inspection found. Facts about the bytes, in the vocabulary of the port rather than of the
/// domain — the decision they lead to is <c>StoredFileContentPolicy</c>'s.
/// </summary>
/// <param name="Status">Whether a scanner reached a verdict, and whether there is a verdict at
/// all.</param>
/// <param name="Head">
/// The leading bytes of the object, at most <see cref="MaxHeadBytes"/> of them, and fewer when the
/// object is shorter. Empty when <see cref="Status"/> is
/// <see cref="ContentInspectionStatus.Unavailable"/>, because then nothing was read.
/// <para>
/// <b>This is the one place a file's content crosses into the application layer, and the bound is
/// the contract rather than an implementation's discretion.</b> No other byte of an upload passes
/// through this process. The rest of the object goes from the store to the scanner inside the
/// adapter, and this layer never sees it.
/// </para>
/// </param>
/// <param name="MalwareSignature">
/// What the scanner named, when <see cref="Status"/> is <see cref="ContentInspectionStatus.Infected"/>;
/// <c>null</c> otherwise.
/// <para>
/// <b>For a log line, never for a response.</b> It is a string chosen by a third party's signature
/// database, and the person it would inform is the operator, not the uploader — telling an uploader
/// which detection fired turns the endpoint into a way of tuning a payload until it passes.
/// </para>
/// </param>
public sealed record ContentInspectionOutcome(
    ContentInspectionStatus Status,
    ReadOnlyMemory<byte> Head,
    string? MalwareSignature)
{
    /// <summary>
    /// How much of an object an implementation reads for <see cref="Head"/>, and the most it may
    /// hand over.
    /// <para>
    /// A kibibyte. Every format signature this template knows sits in the first sixteen bytes; the
    /// size is set by the one thing that has no signature at all and has to be recognised from its
    /// markup — an SVG, whose <c>&lt;svg</c> can sit behind an XML declaration, a doctype and a
    /// comment or two. Generous enough for those, small enough that it is a bounded read whatever
    /// the object weighs.
    /// </para>
    /// </summary>
    public const int MaxHeadBytes = 1024;
}
