using AppTemplate.Application.Core.Common.Ports;

namespace AppTemplate.Application.UnitTests.TestDoubles;

internal sealed class RecordingCacheStore : ICacheStore
{
    private readonly Dictionary<string, object?> _entries = new(StringComparer.Ordinal);
    private readonly List<string> _removed = [];

    internal IReadOnlyList<string> Removed => _removed;

    internal int Misses { get; private set; }

    public async Task<T> GetOrCreateAsync<T>(
        string key,
        Func<CancellationToken, Task<T>> factory,
        TimeSpan lifetime,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(factory);

        if (_entries.TryGetValue(key, out object? kept))
        {
            return (T)kept!;
        }

        Misses++;
        var value = await factory(cancellationToken);
        _entries[key] = value;

        return value;
    }

    public Task RemoveAsync(string key, CancellationToken cancellationToken = default)
    {
        _entries.Remove(key);
        _removed.Add(key);

        return Task.CompletedTask;
    }
}
