namespace AppTemplate.Api.Common.Concurrency;

/// <summary>Whether a caller may change a versioned resource without naming a version.</summary>
public enum IfMatchRequirement
{
    /// <summary>
    /// An unconditional write is accepted. The in-request read-write window is still guarded by the
    /// aggregate's version, so nothing is lost that was protected before; what a caller gives up is
    /// detection of an overwrite decided against a representation it read earlier.
    /// </summary>
    Optional,

    /// <summary>
    /// An unconditional write is refused with 428, so no lost update can go undetected. Every client
    /// must read before it writes and send back what it read.
    /// </summary>
    Required,
}
