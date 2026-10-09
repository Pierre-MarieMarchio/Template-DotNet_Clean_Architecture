using AppTemplate.Application.Features.Files.Ports.FileContentScanner;

namespace AppTemplate.Infrastructure.InMemory.Features.Files;

/// <summary>
/// What the malware scanner would say, arranged per object key — the observable surface of the
/// in-memory scanner. Nothing arranged means clean, which is also what a deployment without a
/// scanner gets.
/// <para>
/// Only the scanner's half is arranged: what a file <em>is</em> still comes from the bytes a test
/// deposited, through the real signature table. Singleton and locked: an integration test may scan
/// from several requests at once.
/// </para>
/// </summary>
public sealed class ArrangedScans
{
    private readonly object _gate = new();

    private readonly Dictionary<string, FileScanOutcome> _verdicts = new(StringComparer.Ordinal);

    /// <summary>The scanner names <paramref name="signature"/> in the object under <paramref name="objectKey"/>.</summary>
    public void Infect(string objectKey, string signature) => Set(objectKey, FileScanOutcome.Infected(signature));

    /// <summary>The scanner refuses the object as past its size limit — permanent, so refused.</summary>
    public void RefuseAsTooLarge(string objectKey) => Set(objectKey, FileScanOutcome.NotInspectable);

    /// <summary>The scanner cannot be reached: no verdict, so the file is left for the next pass.</summary>
    public void MakeUnavailable(string objectKey) => Set(objectKey, FileScanOutcome.Unavailable);

    /// <summary>Forgets every arrangement, so one test's verdicts cannot reach the next.</summary>
    public void Clear()
    {
        lock (_gate)
        {
            _verdicts.Clear();
        }
    }

    /// <summary>What was arranged for a key, or clean when nothing was.</summary>
    internal FileScanOutcome VerdictFor(string objectKey)
    {
        lock (_gate)
        {
            return _verdicts.GetValueOrDefault(objectKey, FileScanOutcome.Clean);
        }
    }

    private void Set(string objectKey, FileScanOutcome outcome)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(objectKey);

        lock (_gate)
        {
            _verdicts[objectKey] = outcome;
        }
    }
}
