namespace AppTemplate.Application.Features.Files.Ports.FileContentScanner;

/// <summary>What a scan found.</summary>
/// <param name="Status">Whether there is a verdict, and which.</param>
/// <param name="MalwareSignature">What the scanner named when infected. For a log line, never a response: it would
/// let an uploader tune a payload until it passes.</param>
public sealed record FileScanOutcome(FileScanStatus Status, string? MalwareSignature)
{
    /// <summary>Nothing found.</summary>
    public static FileScanOutcome Clean { get; } = new(FileScanStatus.Clean, null);

    /// <summary>The scanner refuses this content for good.</summary>
    public static FileScanOutcome NotInspectable { get; } = new(FileScanStatus.NotInspectable, null);

    /// <summary>No verdict this time.</summary>
    public static FileScanOutcome Unavailable { get; } = new(FileScanStatus.Unavailable, null);

    /// <summary>The scanner named <paramref name="signature"/> in the content.</summary>
    public static FileScanOutcome Infected(string signature)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(signature);

        return new FileScanOutcome(FileScanStatus.Infected, signature);
    }
}
