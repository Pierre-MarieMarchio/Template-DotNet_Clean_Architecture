namespace AppTemplate.Application.Features.Files.Ports.FileContentScanner;

/// <summary>
/// How a scan went. A verdict is acted on and never revisited; <see cref="Unavailable"/> is the
/// absence of one and is retried.
/// </summary>
public enum FileScanStatus
{
    /// <summary>Nothing malicious was found — or no scanner is configured, so nothing looked.</summary>
    Clean,

    /// <summary>The scanner named something in the content. Permanent: the bytes under a key never change.</summary>
    Infected,

    /// <summary>
    /// The scanner refuses this content — past the size it accepts. Permanent too, so it is a
    /// verdict: retrying would leave the file waiting for ever.
    /// </summary>
    NotInspectable,

    /// <summary>
    /// No verdict this time: the store or the scanner did not answer, or the budget ran out. Neither
    /// a pass nor a refusal.
    /// </summary>
    Unavailable,
}
