namespace AppTemplate.Api.Common.Jobs;

internal sealed record IntervalPass(JobPass Pass, TimeSpan Interval, bool Enabled);
