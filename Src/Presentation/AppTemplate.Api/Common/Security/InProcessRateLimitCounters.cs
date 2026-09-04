using System.Threading.RateLimiting;

namespace AppTemplate.Api.Common.Security;

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

            // Not part of the budget: queueing is how an implementation refuses, not how much it
            // permits. A caller over budget is told so immediately rather than parked on a request
            // thread, which is the only answer that sheds load instead of holding it.
            QueueLimit = 0,
        };

        // Built here rather than inside the factory, so a budget costs one options object instead of
        // one per partition. Sharing it is safe because nothing mutates it after this line, and the
        // factory runs only when a partition key is met for the first time.
        return httpContext => RateLimitPartition.GetFixedWindowLimiter(
            partitionKey: RateLimiterPartitionKeys.ForAddress(httpContext),
            factory: _ => options);
    }
}
