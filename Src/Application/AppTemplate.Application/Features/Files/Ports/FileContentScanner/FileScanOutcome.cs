namespace AppTemplate.Application.Features.Files.Ports.FileContentScanner;

public sealed record FileScanOutcome(FileScanStatus Status, string? MalwareSignature)
{
    public static FileScanOutcome Clean { get; } = new(FileScanStatus.Clean, null);

    public static FileScanOutcome NotInspectable { get; } = new(FileScanStatus.NotInspectable, null);

    public static FileScanOutcome Unavailable { get; } = new(FileScanStatus.Unavailable, null);

    public static FileScanOutcome Infected(string signature)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(signature);

        return new FileScanOutcome(FileScanStatus.Infected, signature);
    }
}
