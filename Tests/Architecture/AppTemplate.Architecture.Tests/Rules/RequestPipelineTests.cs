using System.Text.RegularExpressions;
using AppTemplate.Architecture.Tests.Fixtures;
using Shouldly;
using Xunit;

namespace AppTemplate.Architecture.Tests.Rules;

public sealed partial class RequestPipelineTests
{
    private static readonly string _pipelinePath = Path.Combine(
        ProjectReferenceGraph.RepositoryRoot,
        "Src",
        "Presentation",
        "AppTemplate.Api",
        "Common",
        "Hosting",
        "HttpPipelineModule.cs");

    private static readonly string _programPath = Path.Combine(
        ProjectReferenceGraph.RepositoryRoot,
        "Src",
        "Presentation",
        "AppTemplate.Api",
        "Program.cs");

    [GeneratedRegex(
        @"^\s*app\.(Use[A-Za-z0-9_]+)\s*\(",
        RegexOptions.Multiline | RegexOptions.CultureInvariant)]
    private static partial Regex Middleware();

    [Fact]
    public void RequestLogging_IsRegisteredBeforeTheSizeLimit()
    {
        var order = MiddlewareOrder();

        int logging = IndexOf(order, "UseApiRequestLogging");
        int limits = IndexOf(order, "UseApiRequestLimits");

        logging.ShouldBeLessThan(
            limits,
            "UseApiRequestLimits answers 413 and returns without calling the next middleware, so "
            + "everything registered after it is skipped on the path it rejects. Registered after "
            + "the limit, request logging never runs for an oversized request, and the one status "
            + "the API can return without any record of it is the one a caller is most likely to "
            + "retry. Order in UseHttpPipeline is the whole of the fix, and the whole of the "
            + "defect.");
    }

    [Fact]
    public void ForwardedHeaders_AreAppliedBeforeAnythingReadsTheRequest()
    {
        var order = MiddlewareOrder();

        order[0].ShouldBe(
            "UseApiForwardedHeaders",
            "the first middleware has to be the one that rewrites the client address and the "
            + $"scheme, and it is '{order[0]}'. Anything ahead of it reads the proxy's address "
            + "instead of the caller's, which partitions the rate limiter by proxy and makes every "
            + "caller behind it share one bucket.");
    }

    [Fact]
    public void TheHostInstallsTheHttpPipeline_BeforeAnyMiddlewareOfItsOwn()
    {
        var order = MiddlewareOrderIn(_programPath, floor: 2);

        order[0].ShouldBe(
            "UseHttpPipeline",
            "the host's first middleware has to be the HTTP pipeline, and it is "
            + $"'{order[0]}'. Anything ahead of it runs before the forwarded headers have been "
            + "applied, so it reads the proxy's address and the proxy's scheme — which is the exact "
            + "defect the ordering inside that pipeline exists to prevent.");
    }

    private static List<string> MiddlewareOrder() => MiddlewareOrderIn(_pipelinePath, floor: 8);

    private static List<string> MiddlewareOrderIn(string path, int floor)
    {
        File.Exists(path).ShouldBeTrue(
            $"'{path}' was not found, so this rule cannot read the pipeline it exists to "
            + "check. What it reads moved and this path did not follow it.");

        var order = Middleware()
            .Matches(File.ReadAllText(path))
            .Select(match => match.Groups[1].Value)
            .ToList();

        order.Count.ShouldBeGreaterThanOrEqualTo(
            floor,
            $"Only {order.Count} middleware registrations were parsed out of '{path}'. They "
            + "are written as 'app.UseSomething(...)' at the start of a line; if that shape changed, "
            + "this pattern has to change with it.");

        return order;
    }

    private static int IndexOf(List<string> order, string middleware)
    {
        int index = order.IndexOf(middleware);

        index.ShouldBeGreaterThanOrEqualTo(
            0,
            $"'{middleware}' is not registered in the HTTP pipeline at all. It was, when this rule "
            + "was written; either it has been renamed and this rule is stale, or the pipeline "
            + "lost it.");

        return index;
    }
}
