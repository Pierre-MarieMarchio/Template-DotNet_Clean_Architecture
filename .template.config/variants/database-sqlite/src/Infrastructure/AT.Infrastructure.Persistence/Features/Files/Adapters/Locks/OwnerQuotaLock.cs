using System.Globalization;
using AppTemplate.Application.Features.Files.Ports.OwnerQuotaLock;
using AppTemplate.Domain.Core.Common.Primitives;
using AppTemplate.Domain.Features.Files.Model.ValueObjects;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;

namespace AppTemplate.Infrastructure.Persistence.Features.Files.Adapters.Locks;

internal sealed class OwnerQuotaLock(string connectionString, ILogger<OwnerQuotaLock> logger, TimeSpan waitLimit)
    : IOwnerQuotaLock
{
    internal static readonly TimeSpan WaitLimit = TimeSpan.FromSeconds(10);

    private static readonly Dictionary<string, Entry> _entries = new(StringComparer.Ordinal);

    private readonly string _database = Path.GetFullPath(new SqliteConnectionStringBuilder(connectionString).DataSource);

    public OwnerQuotaLock(string connectionString, ILogger<OwnerQuotaLock> logger)
        : this(connectionString, logger, WaitLimit)
    {
    }

    public async Task<T> RunExclusivelyAsync<T>(
        UserId owner,
        FileCategory category,
        Func<CancellationToken, Task<T>> work,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(owner);
        ArgumentNullException.ThrowIfNull(category);
        ArgumentNullException.ThrowIfNull(work);

        string key = string.Create(CultureInfo.InvariantCulture, $"{_database}|{owner.Value:D}|{category.Value}");
        var entry = Enter(key);

        try
        {
            if (!await entry.Semaphore.WaitAsync(waitLimit, cancellationToken))
            {
                logger.LogWarning("An owner's quota lock was still held by another upload after the wait limit.");

                throw new OwnerQuotaLockTimeoutException(
                    string.Create(
                        CultureInfo.InvariantCulture,
                        $"The owner's quota lock was not obtained within {waitLimit.TotalSeconds:0} seconds."));
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

    private static Entry Enter(string key)
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

    private static void Leave(string key, Entry entry)
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
