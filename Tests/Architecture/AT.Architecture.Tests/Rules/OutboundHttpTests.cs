using System.Text.RegularExpressions;
using AppTemplate.Architecture.Tests.Fixtures;
using Shouldly;
using Xunit;

namespace AppTemplate.Architecture.Tests.Rules;

public sealed class OutboundHttpTests
{
    private static readonly Regex _construction = new(
        @"\bnew\s+HttpClient\s*\(",
        RegexOptions.None,
        TimeSpan.FromSeconds(5));

    private static readonly Regex _policyInstalled = new(
        @"\bAddOutboundHttp\s*\(",
        RegexOptions.None,
        TimeSpan.FromSeconds(5));

    [Fact]
    public void NoType_ConstructsItsOwnHttpClient()
    {
        var sources = SourceFiles();

        sources.Count.ShouldBeGreaterThanOrEqualTo(
            /*#if (api)*/
            170,
            /*#else
            100,
            #endif*/
            "Far fewer source files were found than this repository holds, so this rule is reading " +
            "the wrong tree and passing for the wrong reason.");

        var offenders = sources
            .Where(source => _construction.IsMatch(File.ReadAllText(source)))
            .Select(RepositoryTree.Relative)
            .Order(StringComparer.Ordinal)
            .ToList();

        offenders.ShouldBeEmpty(
            "A client built with new escapes IHttpClientFactory, and with it every timeout, retry " +
            "and concurrency bound the hosts install on the factory's defaults. It also leaks " +
            "sockets on the pattern that looks most obviously correct. Register a typed client " +
            "instead; it inherits the policy without asking for it.");
    }

    [Fact]
    public void EveryHost_InstallsTheOutboundPolicy()
    {
        var entryPoints = ProjectReferenceGraph.Hosts
            .Select(host => Path.Combine(ProjectReferenceGraph.RootOf(host), "Program.cs"))
            .Order(StringComparer.Ordinal)
            .ToList();

        string[] hosts =
        [
            /*#if (presentationMcp)
            "AppTemplate.PresentationAppMcp",
            #endif*/
            /*#if (presentationBlazorServer)
            "AppTemplate.PresentationAppPortal",
            #endif*/
            //#if (api)
            "AppTemplate.Api",
            //#endif
            /*#if (jobsWorker)
            "AppTemplate.Worker",
            #endif*/
        ];

        ProjectReferenceGraph.Hosts.Select(host => host.Name).Order(StringComparer.Ordinal).ShouldBe(
            hosts.Order(StringComparer.Ordinal),
            "Each host arrives through this list, so that the line below is read about it too: " +
            "the API, each presentation project that is a server host, and the worker when the jobs run apart.");

        var missing = entryPoints
            .Where(entryPoint => !_policyInstalled.IsMatch(File.ReadAllText(entryPoint)))
            .Select(RepositoryTree.Relative)
            .Order(StringComparer.Ordinal)
            .ToList();

        missing.ShouldBeEmpty(
            "A host that does not install the outbound policy hands every client its modules " +
            "register an unbounded timeout and no retry budget. The call belongs beside the module " +
            "composition, in Common/Outbound/.");
    }

    [Fact]
    public void TheConstructionRule_IsSensitive_AndSeesTheCallItForbids()
    {
        _construction.IsMatch("var client = new HttpClient();").ShouldBeTrue();
        _construction.IsMatch("using var client = new HttpClient(handler, disposeHandler: false);")
            .ShouldBeTrue();

        _construction.IsMatch("internal sealed class Adapter(HttpClient client)").ShouldBeFalse();
        _construction.IsMatch("private readonly HttpClient _client;").ShouldBeFalse();
    }

    private static List<string> SourceFiles() =>
        [.. RepositoryTree.SourceFiles(Path.Combine(ProjectReferenceGraph.RepositoryRoot, "Src"), "*.cs", ["bin", "obj"])];
}
