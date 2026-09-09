using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Http;

namespace AppTemplate.Api.Core.Common.Security;

/// <summary>
/// Counters in this process's memory, one fixed window per partition.
/// </summary>
/// <remarks>
/// The default, and the only implementation this template ships: a shared counter would mean a shared
/// store on the path of every request, bought for one capability, in a repository that has already
/// refused a distributed cache twice. The price is that nothing is shared between replicas, which is
/// why <see cref="IRateLimitCounters"/> exists at all.
/// </remarks>
internal sealed class InProcessRateLimitCounters : IRateLimitCounters
{
    public Func<HttpContext, RateLimitPartition<string>> PartitionerFor(RateLimitBudget budget)
    {
        ArgumentNullException.ThrowIfNull(budget);

        var options = new FixedWindowRateLimiterOptions
        {
            PermitLimit = budget.PermitLimit,
            Window = budget.Window,

            // A caller over budget is refused immediately rather than parked on a request thread,
            // which sheds load instead of holding it.
            QueueLimit = 0,
        };

        // One options object for every partition rather than one each. Safe to share: nothing
        // mutates it after this line.
        return httpContext => RateLimitPartition.GetFixedWindowLimiter(
            partitionKey: RateLimiterPartitionKeys.ForAddress(httpContext),
            factory: _ => options);
    }
}
