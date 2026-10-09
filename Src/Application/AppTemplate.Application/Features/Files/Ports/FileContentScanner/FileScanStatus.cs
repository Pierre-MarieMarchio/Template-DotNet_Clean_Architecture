namespace AppTemplate.Application.Features.Files.Ports.FileContentScanner;

/// <summary>
/// How a scan went. A verdict is acted on and never revisited; <see cref="Unavailable"/> is the absence of one and is
/// retried.
/// </summary>
public enum FileScanStatus
{
    /// <summary>Nothing malicious was found, or no scanner is configured and nothing looked.</summary>
    Clean,

    /// <summary>The scanner named something. Permanent: the bytes under a key never change.</summary>
    Infected,

    /// <summary>
    /// The scanner refuses this content (past the size it accepts). Permanent, so a verdict: retrying would park the
    /// file for ever.
    /// </summary>
    NotInspectable,

    /// <summary>No verdict this time: the store or scanner did not answer, or the budget ran out.</summary>
    Unavailable,
}
