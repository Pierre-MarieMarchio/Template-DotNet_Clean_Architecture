namespace AppTemplate.Application.Features.Files.Ports.FileContentInspector;

/// <summary>
/// How an inspection went. The distinction that earns this enum its members is between a verdict and
/// the absence of one, because they lead to opposite actions: a verdict is acted on now and never
/// revisited, and an absence is retried.
/// </summary>
public enum ContentInspectionStatus
{
    /// <summary>
    /// The content was read and nothing malicious was found in it. The head is available, so what
    /// the file actually is can still be decided from it.
    /// <para>
    /// <b>It is only as strong as what was configured.</b> With no scanner configured it means the
    /// bytes were read and nothing more — no scan happened, so nothing could be found. That is the
    /// shipped state of this template and it is deliberate; see the adapter and <c>SECURITY.md</c>
    /// for what a deployment adds. The check that runs unconditionally, with no scanner and no
    /// configuration at all, is the one on what the content is.
    /// </para>
    /// </summary>
    Clean,

    /// <summary>
    /// A scanner examined the content and named something in it. A verdict, and a permanent one:
    /// the bytes under a key never change.
    /// </summary>
    Infected,

    /// <summary>
    /// A scanner is configured and refused to look at this object — it is past the stream limit that
    /// scanner accepts, which no retry will change. Also a verdict, in the sense that matters: it
    /// will still be true on the next pass, so treating it as a transient failure would leave the
    /// file waiting for ever.
    /// </summary>
    NotInspectable,

    /// <summary>
    /// No verdict, this time. The store did not answer, the scanner is unreachable, or the call ran
    /// out of budget. <b>The caller must not read this as either a pass or a refusal</b> — it is the
    /// case where fail-open would serve unexamined content and fail-closed would destroy a file over
    /// somebody else's outage, and the only answer that is neither is to leave the file where it is
    /// and ask again.
    /// </summary>
    Unavailable,
}
