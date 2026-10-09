using AppTemplate.Application.Features.Files.Ports.OwnerQuotaLock;
using AppTemplate.Domain.Core.Common.Primitives;
using AppTemplate.Domain.Features.Files.ValueObjects;

namespace AppTemplate.Infrastructure.InMemory.Features.Files;

/// <summary>
/// An <see cref="IOwnerQuotaLock"/> held in this process only: one semaphore per (owner, category),
/// dropped when nobody holds or waits for it. Serialises the registrations of one host, never of
/// several, so it stands in for the database lock only where a single process runs.
/// </summary>
internal sealed class InMemoryOwnerQuotaLock(TimeSpan waitLimit) : IOwnerQuotaLock
{
    /// <summary>The wait limit of the PostgreSQL adapter.</summary>
    internal static readonly TimeSpan DefaultWaitLimit = TimeSpan.FromSeconds(10);

    private readonly Dictionary<(Guid Owner, string Category), Entry> _entries = [];

    public async Task<T> RunExclusivelyAsync<T>(
        UserId owner,
        FileCategory category,
        Func<CancellationToken, Task<T>> work,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(owner);
        ArgumentNullException.ThrowIfNull(category);
        ArgumentNullException.ThrowIfNull(work);

        var key = (owner.Value, category.Value);
        var entry = Enter(key);

        try
        {
            if (!await entry.Semaphore.WaitAsync(waitLimit, cancellationToken))
            {
                throw new OwnerQuotaLockTimeoutException();
            }

            try
            {
                return await work(cancellationToken);
            }
            finally
            {
                entry.Semaphore.Release();
            }
        }
        finally
        {
            Leave(key, entry);
        }
    }

    private Entry Enter((Guid, string) key)
    {
        lock (_entries)
        {
            if (!_entries.TryGetValue(key, out var entry))
            {
                entry = new Entry();
                _entries[key] = entry;
            }

            entry.Users++;

            return entry;
        }
    }

    private void Leave((Guid, string) key, Entry entry)
    {
        lock (_entries)
        {
            if (--entry.Users == 0)
            {
                _entries.Remove(key);
                entry.Semaphore.Dispose();
            }
        }
    }

    private sealed class Entry
    {
        public SemaphoreSlim Semaphore { get; } = new(1, 1);

        public int Users { get; set; }
    }
}
