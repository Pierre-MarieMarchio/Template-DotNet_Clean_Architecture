namespace AppTemplate.Application.Features.Files.Ports.FileContentInspector;

/// <summary>
/// Reads the bytes stored under a key and reports what it found in them.
/// <para>
/// <b>One port for two questions, because they are one question asked of one stream.</b> "What is
/// this content really?" and "is there malware in it?" are asked of the same object, at the same
/// moment, by the same read. Two ports would mean two reads of up to five gigabytes, two calls to
/// sequence, and two failures to reconcile before anything could be decided; one port means one
/// pass over the bytes and one verdict to act on. The split that would be worth making is by
/// <em>capability an implementer can satisfy independently</em>, and there is none here: nothing can
/// answer either question without opening the object.
/// </para>
/// <para>
/// <b>It reports findings and decides nothing.</b> What the leading bytes say the file is, whether
/// that disagreeing with the declared type is a refusal, whether an SVG is acceptable, whether an
/// unexaminable file may be released — all of that is <c>StoredFileContentPolicy</c>, in this layer,
/// where it can be read and tested without a scanner. An adapter that returned "reject" would be
/// deciding what a failure means on the far side of the port, which is the exact shape
/// <c>PortConventionTests</c> exists to forbid. The division of labour is that an implementation
/// does what needs a socket — reaching the object, streaming it past a scanner — and this layer does
/// what needs a table of constants.
/// </para>
/// <para>
/// <b>It takes a key rather than a stream, and that is forced.</b> Handing this layer the whole
/// object would mean a fifth operation on <see cref="FileContentStore.IFileContentStore"/>, which is
/// at the four-operation ceiling, and would put a five-gigabyte file inside the application layer —
/// the one thing this whole feature is arranged to avoid. So the adapter opens the object itself,
/// and what comes back across is a bounded prefix of it. The consequence is that an adapter for this
/// port needs a way to read the store, which is why the real one lives in the module that already
/// has one.
/// </para>
/// <para>
/// <b>Nothing here decompresses anything, and an implementation must keep it that way in its own
/// process.</b> <c>SECURITY.md</c> records that decompression bombs are not addressed because
/// nothing unpacks an archive; an inspection that unpacked one would open that hole rather than
/// close another. Bounding the output size and the time <em>before</em> reading is the rule, and an
/// implementation that delegates unpacking to a scanner has moved the bound into that scanner's
/// configuration rather than removed the need for it.
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
