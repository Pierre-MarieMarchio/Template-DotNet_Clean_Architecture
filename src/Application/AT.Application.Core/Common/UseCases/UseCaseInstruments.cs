using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace AppTemplate.Application.Core.Common.UseCases;

public static class UseCaseInstruments
{
    public const string Name = "AppTemplate.UseCases";

    public const string UseCaseTag = "use_case";

    public const string OutcomeTag = "outcome";

    public const string ErrorCodeTag = "error_code";

    public static readonly ActivitySource ActivitySource = new(Name);

    private static readonly Meter _meter = new(Name);

    public static readonly Histogram<double> Duration = _meter.CreateHistogram<double>(
        "apptemplate.use_cases.duration",
        unit: "s",
        description: "Time a use case took from its message being sent to its answer, tagged by use case and outcome (success, failure, exception).");
}
