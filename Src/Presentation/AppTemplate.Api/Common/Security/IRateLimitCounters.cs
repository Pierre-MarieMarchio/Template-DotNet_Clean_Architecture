using System.Threading.RateLimiting;

namespace AppTemplate.Api.Common.Security;

/// <summary>
/// Where the rate limiter's counters live. Given a budget, it hands back the partitioner that assigns
/// a request to the counter it spends against.
/// </summary>
/// <remarks>
/// <para>
/// An implementation chooses one thing only: who holds the count. The key policy stays in
/// <see cref="RateLimiterPartitionKeys"/>, and the refusal — status code, problem document,
/// <c>Retry-After</c> — stays in <see cref="RateLimitingExtensions"/>, so two implementations cannot
/// disagree about what a partition is or about what a refused caller is told. The shipped one,
/// <see cref="InProcessRateLimitCounters"/>, keeps its counters in one process's memory, so the limit
/// a caller actually meets is the configured number multiplied by the replica count;
/// <c>docs/CONFIGURATION.md</c> prints that arithmetic.
/// </para>
/// <para>
/// A distributed implementation returns a partitioner over <c>RateLimitPartition.Get(key, …)</c> with
/// a <see cref="RateLimiter"/> of its own that talks to a shared store. Two obligations come with
/// that and neither is enforceable from here: its lease must carry
/// <see cref="MetadataName.RetryAfter"/>, or the <c>Retry-After</c> header this API promises silently
/// stops being written; and it must decide, in code, what a caller gets when the shared store is
/// unreachable.
/// </para>
/// </remarks>
internal interface IRateLimitCounters
{
    /// <summary>
    /// The partitioner for <paramref name="budget"/>: a request in, the partition it spends against
    /// out. Called once per budget while the host is composed, never per request.
    /// </summary>
    Func<HttpContext, RateLimitPartition<string>> PartitionerFor(RateLimitBudget budget);
}
