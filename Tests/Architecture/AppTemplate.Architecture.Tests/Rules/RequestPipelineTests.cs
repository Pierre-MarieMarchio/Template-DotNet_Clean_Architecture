using System.Text.RegularExpressions;
using AppTemplate.Architecture.Tests.Fixtures;
using Shouldly;
using Xunit;

namespace AppTemplate.Architecture.Tests.Rules;

/// <summary>
/// Three orderings in the API's request pipeline that change what a caller receives, and that no
/// compiler and no unit test can see: middleware order is the order of statements, and getting it
/// wrong produces a working application that is quietly missing a behaviour rather than one that
/// fails.
/// </summary>
/// <remarks>
/// Two files, because the pipeline is written in one and installed by the other. The order of the
/// middleware itself is <c>UseCorePipeline</c>'s, in the SDK; whether a host puts anything ahead of
/// that call is the host's own, and the third rule below is what keeps the first two from being
/// true of a pipeline something else already ran in front of. Both are read from source, because
/// this test project deliberately references neither — the same reason <c>HttpSurfaceTests</c> and
/// <c>ObservabilityRegistrationTests</c> read their subjects that way.
/// </remarks>
public sealed class RequestPipelineTests
{
    private static readonly string _pipelinePath = Path.Combine(
        ProjectReferenceGraph.RepositoryRoot,
        "Src",
        "Presentation",
        "AppTemplate.Api.Core",
        "ApiCoreModule.cs");

    private static readonly string _programPath = Path.Combine(
        ProjectReferenceGraph.RepositoryRoot,
        "Src",
        "Presentation",
        "AppTemplate.Api",
        "Program.cs");

    /// <summary>Matches <c>app.UseSomething(</c> at the start of a statement.</summary>
    private static readonly Regex _middleware = new(
        @"^\s*app\.(Use[A-Za-z0-9_]+)\s*\(",
        RegexOptions.Multiline | RegexOptions.Compiled | RegexOptions.CultureInvariant);

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
            + "retry. Order in UseCorePipeline is the whole of the fix, and the whole of the "
            + "defect.");
    }

    /// <summary>
    /// The other ordering the file argues for in prose: the rate limiter partitions on the remote
    /// address, and CORS, authentication and the exception handler all read the scheme. Both are
    /// wrong until the forwarded headers have been applied.
    /// </summary>
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

    /// <summary>
    /// The first two rules are about the order inside <c>UseCorePipeline</c>. They say nothing about
    /// what a host runs before calling it, and a host that installs a middleware of its own first
    /// would defeat the ordering below without either of them noticing — the pipeline would still be
    /// internally correct and the caller's address would still be the proxy's.
    /// </summary>
    [Fact]
    public void TheHostInstallsTheCorePipeline_BeforeAnyMiddlewareOfItsOwn()
    {
        var order = MiddlewareOrderIn(_programPath, floor: 3);

        order[0].ShouldBe(
            "UseCorePipeline",
            "the host's first middleware has to be the core pipeline, and it is "
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

        var order = _middleware
            .Matches(File.ReadAllText(path))
            .Select(match => match.Groups[1].Value)
            .ToList();

        // The floor these rules need. A pattern that has stopped matching finds nothing, and the
        // assertions would then be about an empty list: one comparing two -1s, the others reading
        // past the end. Neither would say what actually went wrong.
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
            $"'{middleware}' is not registered in the core pipeline at all. It was, when this rule "
            + "was written; either it has been renamed and this rule is stale, or the pipeline "
            + "lost it.");

        return index;
    }
}
