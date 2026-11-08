namespace AppTemplate.Application.Core.Common.Ports;

public interface ILeaderLease
{
    Task<bool> TryRunExclusivelyAsync(
        string leaseName,
        Func<CancellationToken, Task> work,
        CancellationToken cancellationToken = default);
}
