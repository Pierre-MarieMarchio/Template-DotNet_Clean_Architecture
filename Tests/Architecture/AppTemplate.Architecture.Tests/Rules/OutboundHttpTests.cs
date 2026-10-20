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
            170,
            "Far fewer source files were found than this repository holds, so this rule is reading " +
            "the wrong tree and passing for the wrong reason.");

        var offenders = sources
            .Where(source => _construction.IsMatch(File.ReadAllText(source)))
            .Select(Relative)
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
        var entryPoints = Directory
            .EnumerateFiles(
                Path.Combine(ProjectReferenceGraph.RepositoryRoot, "Src", "Presentation"),
                "Program.cs",
                SearchOption.AllDirectories)
            .Where(IsProductionSource)
            .Order(StringComparer.Ordinal)
            .ToList();

        entryPoints.Count.ShouldBe(
            1,
            "AppTemplate.Api is the one host this repository has, and it runs the background jobs " +
            "too. A second one is welcome, but it has to arrive through this number so that the " +
            "line below is read about it too.");

        var missing = entryPoints
            .Where(entryPoint => !_policyInstalled.IsMatch(File.ReadAllText(entryPoint)))
            .Select(Relative)
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
        [.. Directory
            .EnumerateFiles(
                Path.Combine(ProjectReferenceGraph.RepositoryRoot, "Src"),
                "*.cs",
                SearchOption.AllDirectories)
            .Where(IsProductionSource)];

    private static bool IsProductionSource(string file) =>
        !file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
        && !file.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal);

    private static string Relative(string file) =>
        Path.GetRelativePath(ProjectReferenceGraph.RepositoryRoot, file).Replace('\\', '/');
}
