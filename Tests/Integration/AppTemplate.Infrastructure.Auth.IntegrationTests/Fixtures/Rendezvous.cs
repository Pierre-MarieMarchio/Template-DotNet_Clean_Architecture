namespace AppTemplate.Infrastructure.Auth.IntegrationTests.Fixtures;

internal sealed class Rendezvous(int participants)
{
    private readonly TaskCompletionSource _released = new(TaskCreationOptions.RunContinuationsAsynchronously);

    private int _arrived;

    public Task ArriveAsync(CancellationToken cancellationToken)
    {
        if (Interlocked.Increment(ref _arrived) >= participants)
        {
            _released.TrySetResult();
        }

        return _released.Task.WaitAsync(cancellationToken);
    }
}
