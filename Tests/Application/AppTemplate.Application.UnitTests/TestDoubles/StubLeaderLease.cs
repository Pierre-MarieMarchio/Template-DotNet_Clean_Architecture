using AppTemplate.Application.Core.Common.Ports;

namespace AppTemplate.Application.UnitTests.TestDoubles;

/// <summary>
/// A lease that is either always granted — the work runs, awaited, inside the call — or always held
/// by another host, and that records the names it was asked for.
/// </summary>
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
