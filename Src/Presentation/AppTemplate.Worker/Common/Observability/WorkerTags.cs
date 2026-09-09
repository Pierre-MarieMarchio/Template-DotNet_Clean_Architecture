namespace AppTemplate.Worker.Common.Observability;

/// <summary>
/// The tag keys every worker loop breaks its counters down by.
/// <para>
/// Here rather than beside each feature's instruments, because the value of these keys is that they
/// are the <em>same</em> key in all three loops: a dashboard asks for reminder, file and maintenance
/// passes broken down by outcome in one query, and it can only do that while the three agree on the
/// spelling. Twelve call sites wrote <c>"outcome"</c> as a literal, and a thirteenth that wrote
/// <c>"result"</c> would have split the series in two without failing anything.
/// </para>
/// </summary>
internal static class WorkerTags
{
    /// <summary>How a pass ended: <c>success</c>, <c>failure</c>, <c>exception</c> or <c>disabled</c>.</summary>
    public const string Outcome = "outcome";

    /// <summary>Which named task inside a loop the measurement belongs to.</summary>
    public const string Task = "task";
}
