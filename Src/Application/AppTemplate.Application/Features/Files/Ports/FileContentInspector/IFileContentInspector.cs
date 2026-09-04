namespace AppTemplate.Application.Features.Files.Ports.FileContentInspector;

/// <summary>
/// Reads the bytes stored under a key and reports what it found in them. One port answers both
/// questions — what the content really is, and whether there is malware in it — because they are
/// asked of the same object by the same read, and nothing can answer either without opening it.
/// <para>
/// <b>It reports findings and decides nothing.</b> What the leading bytes say the file is, whether
/// that disagreeing with the declared type is a refusal, whether an SVG is acceptable, whether an
/// unexaminable file may be released — all of that is <c>StoredFileContentPolicy</c>, in this layer,
/// where it can be read and tested without a scanner. An adapter that returned "reject" would be
/// deciding what a failure means on the far side of the port, which is the exact shape
/// <c>PortConventionTests</c> exists to forbid.
/// </para>
/// <para>
/// <b>It takes a key rather than a stream</b>, so that a five-gigabyte object never enters the
/// application layer: the adapter opens the object itself and what comes back across is a bounded
/// prefix of it. An adapter therefore needs its own way to read the store, which is why the real
/// one lives in the module that already has one.
/// </para>
/// <para>
/// <b>Nothing here decompresses anything, and an implementation must keep it that way in its own
/// process.</b> <c>SECURITY.md</c> records that decompression bombs are unaddressed because nothing
/// unpacks an archive; an inspection that unpacked one would open that hole. Bounding the output
/// size and the time <em>before</em> reading is the rule, and delegating unpacking to a scanner
/// moves that bound into the scanner's configuration rather than removing the need for it.
/// </para>
/// </summary>
public interface IFileContentInspector
{
    /// <summary>
    /// Examines the object stored under <paramref name="objectKey"/>.
    /// <para>
    /// It never throws to report a verdict. A store that cannot be reached, a scanner that is down
    /// and content nothing can examine are all answers — see <see cref="ContentInspectionStatus"/> —
    /// because the caller has to tell "refuse this file" from "ask again later", and an exception
    /// collapses the two into one thing a sweep would treat as a failed pass.
    /// </para>
    /// </summary>
    Task<ContentInspectionOutcome> InspectAsync(string objectKey, CancellationToken cancellationToken = default);
}
