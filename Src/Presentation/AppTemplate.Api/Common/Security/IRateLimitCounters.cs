using System.Threading.RateLimiting;

namespace AppTemplate.Api.Common.Security;

/// <summary>
/// Where the rate limiter's counters live. Given a budget, it hands back the partitioner that assigns
/// a request to the counter it spends against.
/// </summary>
/// <remarks>
/// <para>
/// <b>This exists to be replaced, and nothing here replaces it.</b> The shipped implementation,
/// <see cref="InProcessRateLimitCounters"/>, keeps its counters in one process's memory, so the limit
/// a caller actually meets is the configured number multiplied by the replica count. That is a stated
/// trade rather than a defect — <c>docs/CONFIGURATION.md</c> prints the multiplication — but it is
/// the one property of this limiter a deployment cannot fix from outside, and before this interface
/// there was no place to fix it from inside either: the budget, the partition key and the counter
/// were one expression inside <see cref="RateLimitingExtensions"/>. Splitting the counter out is the
/// whole of what this type does.
/// </para>
/// <para>
/// What deliberately stays <em>outside</em> it is as much of the design as what it holds.
/// <see cref="RateLimiterPartitionKeys"/> keeps the key policy, because two implementations that
/// disagreed on what a partition is would not be two ways of counting the same thing. The refusal —
/// status code, problem document, <c>Retry-After</c> — stays in <see cref="RateLimitingExtensions"/>,
/// because what a client is told is a property of this API and not of where a number is kept. So an
/// implementation of this interface chooses one thing only: who holds the count.
/// </para>
/// <para>
/// It hands back a partitioner rather than a partition so that a budget is read once, when the host
/// is composed, and not on every request. The limiter is the one component here that has to stay
/// cheap while it is being attacked — <see cref="RateLimiterPartitionKeys"/> makes the same argument
/// about why authentication cannot run before it — and a seam that allocated a closure per request
/// would be spending on the path whose whole job is to refuse.
/// </para>
/// <para>
/// A distributed implementation returns a partitioner over <c>RateLimitPartition.Get(key, …)</c> with
/// a <see cref="RateLimiter"/> of its own that talks to a shared store. Two obligations come with
/// that and neither is enforceable from here: its lease must carry
/// <see cref="MetadataName.RetryAfter"/>, or the <c>Retry-After</c> header this API promises silently
/// stops being written; and it must decide, in code, what a caller gets when the shared store is
/// unreachable — the in-process limiter has no such state to lose, so nothing in this file has had to
/// answer that question yet.
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
