using Shouldly;
using Xunit;

namespace AppTemplate.Api.UnitTests.TestSupport;

internal static class BackgroundServiceProbe
{
    private static readonly TimeSpan _ceiling = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan _pollInterval = TimeSpan.FromMilliseconds(10);

    internal static async Task WaitUntilAsync(Func<bool> condition, string description)
    {
        ArgumentNullException.ThrowIfNull(condition);

        using var timeout = new CancellationTokenSource(_ceiling);

        while (!condition())
        {
            if (timeout.IsCancellationRequested)
            {
                condition().ShouldBeTrue(
                    $"Waited {_ceiling.TotalSeconds:N0}s for {description} and it never happened.");
                return;
            }

            await Task.Delay(_pollInterval, TestContext.Current.CancellationToken);
        }
    }
}
