using Shouldly;
using Xunit;

namespace AppTemplate.Worker.UnitTests.TestSupport;

/// <summary>
/// Waiting for a <c>BackgroundService</c> to have run, without asserting how long that takes.
/// <para>
/// Three test classes had written this loop identically, each with its own five-second ceiling, and
/// five seconds is a bet on the machine rather than a property of the code: the whole solution runs
/// eleven test projects at once, one of them starting a PostgreSQL container, and a first
/// <c>PeriodicTimer</c> tick that normally lands in 20 ms can miss that budget under the load. The
/// ceiling exists only to stop a broken test hanging forever, so it is generous; a passing test
/// leaves as soon as the condition holds and costs nothing.
/// </para>
/// </summary>
internal static class BackgroundServiceProbe
{
    private static readonly TimeSpan _ceiling = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan _pollInterval = TimeSpan.FromMilliseconds(10);

    /// <param name="description">
    /// What was being waited for, in the reader's words. Without it a timeout surfaces as a bare
    /// <c>OperationCanceledException</c> and the failure says nothing about what did not happen.
    /// </param>
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
