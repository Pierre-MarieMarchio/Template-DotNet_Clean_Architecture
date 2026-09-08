using AppTemplate.Application.Core.Common.Ports;

namespace AppTemplate.Application.UnitTests.TestDoubles;

/// <summary>
/// An <see cref="ICacheStore"/> that keeps what it was given and records what was dropped, so a
/// test can assert both that a read was served and that a write invalidated it.
/// </summary>
/// <remarks>
/// A real store rather than a substitute: a substitute's <c>GetOrCreateAsync</c> hands back
/// <c>default</c>, which is a null list every caller here would then work with. Not thread-safe,
/// and does not need to be — one test, one caller.
/// </remarks>
internal sealed class RecordingCacheStore : ICacheStore
{
    private readonly Dictionary<string, object?> _entries = new(StringComparer.Ordinal);
    private readonly List<string> _removed = [];

    /// <summary>The keys dropped, in order, including any dropped more than once.</summary>
    internal IReadOnlyList<string> Removed => _removed;

    /// <summary>How many times the factory ran, which is how many reads were misses.</summary>
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
