namespace AppTemplate.Infrastructure.Identity.UnitTests.Fixtures;

/// <summary>
/// Holds a fixed number of callers until all of them have arrived, then releases them together.
/// </summary>
/// <remarks>
/// <c>Task.WhenAll</c> over two calls proves nothing about concurrency: an asynchronous call that
/// never yields runs to completion before the second one starts, and the "race" becomes two
/// sequential operations. This makes the overlap a fact rather than a hope — every participant is
/// known to be past the read and not yet at the write when the last one arrives.
/// </remarks>
internal sealed class Rendezvous(int participants)
{
    private readonly TaskCompletionSource _released = new(TaskCreationOptions.RunContinuationsAsynchronously);

    private int _arrived;

    /// <summary>
    /// Waits for the rest. The token is honoured so that a participant which never arrives fails the
    /// test on its timeout instead of hanging the run.
    /// </summary>
    public Task ArriveAsync(CancellationToken cancellationToken)
    {
        if (Interlocked.Increment(ref _arrived) >= participants)
        {
            _released.TrySetResult();
        }

        return _released.Task.WaitAsync(cancellationToken);
    }
}
