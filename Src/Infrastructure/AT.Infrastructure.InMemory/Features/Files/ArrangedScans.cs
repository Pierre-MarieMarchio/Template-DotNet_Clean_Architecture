using AppTemplate.Application.Features.Files.Ports.FileContentScanner;

namespace AppTemplate.Infrastructure.InMemory.Features.Files;

public sealed class ArrangedScans
{
    private readonly object _gate = new();

    private readonly Dictionary<string, FileScanOutcome> _verdicts = new(StringComparer.Ordinal);

    public void Infect(string objectKey, string signature) => Set(objectKey, FileScanOutcome.Infected(signature));

    public void RefuseAsTooLarge(string objectKey) => Set(objectKey, FileScanOutcome.NotInspectable);

    public void MakeUnavailable(string objectKey) => Set(objectKey, FileScanOutcome.Unavailable);

    public void Clear()
    {
        lock (_gate)
        {
            _verdicts.Clear();
        }
    }

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
