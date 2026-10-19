using AppTemplate.Application.Features.Files.Ports.OwnerQuotaLock;
using AppTemplate.Domain.Core.Common.Primitives;
using AppTemplate.Domain.Features.Files.Model.ValueObjects;

namespace AppTemplate.Application.UnitTests.TestDoubles;

/// <summary>
/// One process-wide lock for every key, recording who asked and whether the work ran while held.
/// <see cref="Contended"/> completes when a caller has to wait, so a test can interleave without
/// sleeping.
/// </summary>
internal sealed class RecordingOwnerQuotaLock : IOwnerQuotaLock
{
    private readonly Queue<TaskCompletionSource> _waiters = new();
    private bool _taken;
    private readonly TaskCompletionSource _contended = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private int _calls;
    private int _held;

    /// <summary>When set, every call throws <see cref="OwnerQuotaLockTimeoutException"/> without running the work.</summary>
    public bool TimesOut { get; init; }

    public int Calls => Volatile.Read(ref _calls);

    public bool IsHeld => Volatile.Read(ref _held) == 1;

    public List<(UserId Owner, FileCategory Category)> Keys { get; } = [];

    public Task Contended => _contended.Task;

    public async Task<T> RunExclusivelyAsync<T>(
        UserId owner,
        FileCategory category,
        Func<CancellationToken, Task<T>> work,
        CancellationToken cancellationToken = default)
    {
        Interlocked.Increment(ref _calls);

        lock (Keys)
        {
            Keys.Add((owner, category));
        }

        if (TimesOut)
        {
            throw new OwnerQuotaLockTimeoutException();
        }

        await AcquireAsync().WaitAsync(cancellationToken);

        Volatile.Write(ref _held, 1);

        try
        {
            return await work(cancellationToken);
        }
        finally
        {
            Volatile.Write(ref _held, 0);
            Release();
        }
    }

    private Task AcquireAsync()
    {
        lock (_waiters)
        {
            if (!_taken)
            {
                _taken = true;

                return Task.CompletedTask;
            }

            var waiter = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            _waiters.Enqueue(waiter);
            _contended.TrySetResult();

            return waiter.Task;
        }
    }

    private void Release()
    {
        lock (_waiters)
        {
            if (_waiters.TryDequeue(out var next))
            {
                next.SetResult();
            }
            else
            {
                _taken = false;
            }
        }
    }
}
