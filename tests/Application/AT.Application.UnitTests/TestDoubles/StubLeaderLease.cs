using AppTemplate.Application.Core.Common.Ports;

namespace AppTemplate.Application.UnitTests.TestDoubles;

internal sealed class StubLeaderLease(bool granted = true) : ILeaderLease
{
    public List<string> RequestedNames { get; } = [];

    public static StubLeaderLease HeldElsewhere() => new(granted: false);

    public async Task<bool> TryRunExclusivelyAsync(
        string leaseName,
        Func<CancellationToken, Task> work,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(work);

        RequestedNames.Add(leaseName);

        if (!granted)
        {
            return false;
        }

        await work(cancellationToken);

        return true;
    }
}
