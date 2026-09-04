using System.Text.RegularExpressions;
using AppTemplate.Architecture.Tests.Fixtures;
using Shouldly;
using Xunit;

namespace AppTemplate.Architecture.Tests.Rules;

/// <summary>
/// The outbound HTTP budget, and the two ways a call can escape it.
/// <para>
/// The policy is installed on <c>IHttpClientFactory</c>'s defaults, once per host, so a module that
/// registers a typed client inherits it without knowing it exists and cannot opt out by forgetting.
/// That leaves exactly two holes, and these rules are each one of them: a client built with
/// <c>new</c> never meets the factory at all, and a host that never installs the defaults gives
/// every client in it no budget whatsoever.
/// </para>
/// <para>
/// Read from the source tree rather than from metadata, deliberately. In IL there is no difference
/// between constructing an <c>HttpClient</c> and receiving one as a constructor parameter — both are
/// a dependency on the same type — and receiving one is what every typed client does. The
/// distinction this rule is about exists only in the text.
/// </para>
/// </summary>
public sealed class OutboundHttpTests
{
    /// <summary>
    /// Matches construction, not use. The trailing <c>(</c> is what separates <c>new HttpClient(…)</c>
    /// from a parameter or a field declared as <c>HttpClient</c>, and <c>\s*</c> covers the form a
    /// formatter may leave behind.
    /// </summary>
    private static readonly Regex _construction = new(
        @"\bnew\s+HttpClient\s*\(",
        RegexOptions.None,
        TimeSpan.FromSeconds(5));

    /// <summary>
    /// The composition call each host owes. Named per host — <c>AddApiOutboundHttp</c>,
    /// <c>AddWorkerOutboundHttp</c> — because the two files are twins that must not drift, so the
    /// match is on the shared part rather than on either name.
    /// </summary>
    private static readonly Regex _policyInstalled = new(
        @"\bAdd\w*OutboundHttp\s*\(",
        RegexOptions.None,
        TimeSpan.FromSeconds(5));

    [Fact]
    public void NoType_ConstructsItsOwnHttpClient()
    {
        var sources = SourceFiles();

        sources.Count.ShouldBeGreaterThanOrEqualTo(
            300,
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

    /// <summary>
    /// Every host installs the policy. The rule that matters most as hosts are added: the defaults
    /// only apply to the container that was told about them, so a third entry point composing the
    /// same modules would give every client in it no budget at all — and nothing else in this
    /// repository would notice, because the modules are unchanged and their tests still pass.
    /// </summary>
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
            2,
            "AppTemplate.Api and AppTemplate.Worker are the hosts this repository has. A third one " +
            "is welcome, but it has to arrive through this number so that the line below is read " +
            "about it too.");

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

    /// <summary>
    /// Proves the construction matcher is live. Without this, a regex that had stopped matching
    /// anything — a renamed type, a stray escape — would leave the rule above passing over every
    /// file in the repository and guaranteeing nothing.
    /// </summary>
    [Fact]
    public void TheConstructionRule_IsSensitive_AndSeesTheCallItForbids()
    {
        _construction.IsMatch("var client = new HttpClient();").ShouldBeTrue();
        _construction.IsMatch("using var client = new HttpClient(handler, disposeHandler: false);")
            .ShouldBeTrue();

        // What it must not match: receiving one, which is exactly what a typed client does.
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
