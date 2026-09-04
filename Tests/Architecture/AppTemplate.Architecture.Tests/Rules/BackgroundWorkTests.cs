using System.Text.RegularExpressions;
using AppTemplate.Architecture.Tests.Fixtures;
using Shouldly;
using Xunit;

namespace AppTemplate.Architecture.Tests.Rules;

/// <summary>
/// Where a guarantee about running once across the fleet is allowed to live.
/// <para>
/// Read from the source tree rather than from metadata, because this project deliberately does not
/// reference <c>AppTemplate.Worker</c> — it composes the worker's modules itself, so that the rules
/// here stay about the modules rather than about one host. The background services are therefore
/// invisible to reflection from here, and the text is what is left.
/// </para>
/// </summary>
public sealed class BackgroundWorkTests
{
    /// <summary>
    /// A constructor parameter, not a mention. The distinction is not pedantry: the first loop this
    /// rule was written for names the port in its own documentation, to say that it deliberately
    /// does <em>not</em> take one — and a matcher that could not tell the two apart would punish
    /// the file for explaining itself.
    /// </summary>
    private static readonly Regex _leaseDependency = new(
        @"\bILeaderLease\s+[a-z_][A-Za-z0-9_]*",
        RegexOptions.None,
        TimeSpan.FromSeconds(5));

    /// <summary>A line comment or a documentation line, which carries prose rather than code.</summary>
    private static readonly Regex _commentLine = new(
        @"^\s*(?://|///).*$",
        RegexOptions.Multiline,
        TimeSpan.FromSeconds(5));

    private static string CodeOf(string file) =>
        _commentLine.Replace(File.ReadAllText(file), string.Empty);

    /// <summary>
    /// A <c>BackgroundService</c> is a trigger, not the operation. A lease taken there protects the
    /// timer's callers and nobody else — and the two maintenance purges are already exposed over
    /// HTTP by <c>MaintenanceController</c>, which is the standing proof that a second caller turns
    /// up. So the guard belongs in the use case, where <c>FireDueRemindersUseCase</c> puts it, and
    /// this rule is what keeps the next loop from putting it back in the timer because that is the
    /// easier place to reach.
    /// </summary>
    [Fact]
    public void NoBackgroundService_TakesTheLeaderLease()
    {
        var services = BackgroundServices();

        services.Count.ShouldBeGreaterThanOrEqualTo(
            3,
            "Fewer background services were found than this template ships, so this rule is " +
            "reading the wrong tree and passing for the wrong reason.");

        var offenders = services
            .Where(service => _leaseDependency.IsMatch(CodeOf(service)))
            .Select(Relative)
            .Order(StringComparer.Ordinal)
            .ToList();

        offenders.ShouldBeEmpty(
            "Exclusivity between hosts is a property of the operation, not of the timer that starts " +
            "it. Move the lease into the use case, the way FireDueRemindersUseCase does, so that a " +
            "second caller of the same operation is covered too.");
    }

    /// <summary>
    /// Proves the matcher is live. Without it, a renamed port would leave the rule above reading
    /// every background service and finding nothing, for ever.
    /// </summary>
    [Fact]
    public void TheLeaseRule_IsSensitive_AndSeesTheDependencyItForbids()
    {
        _leaseDependency.IsMatch("internal sealed class Loop(ILeaderLease lease) : BackgroundService")
            .ShouldBeTrue();

        _leaseDependency.IsMatch("internal sealed class Loop(IServiceScopeFactory factory)")
            .ShouldBeFalse();

        // A file that names the port only to say it does not take one must stay clean.
        _commentLine.Replace("/// Neither loop takes ILeaderLease lease, and that is deliberate.", string.Empty)
            .ShouldBeEmpty();
    }

    /// <summary>
    /// The application layer is where the lease is allowed to be, and this asserts it actually is —
    /// so that the rule above cannot be satisfied by nobody using the port at all.
    /// </summary>
    [Fact]
    public void TheLeaderLease_IsTakenByAUseCase()
    {
        string useCases = Path.Combine(
            ProjectReferenceGraph.RepositoryRoot,
            "Src",
            "Application",
            "AppTemplate.Application",
            "Features");

        var consumers = Directory
            .EnumerateFiles(useCases, "*UseCase.cs", SearchOption.AllDirectories)
            .Where(IsProductionSource)
            .Where(useCase => _leaseDependency.IsMatch(CodeOf(useCase)))
            .Select(Relative)
            .Order(StringComparer.Ordinal)
            .ToList();

        consumers.ShouldNotBeEmpty(
            "No use case takes the leader lease, so the rule above is forbidding background " +
            "services from doing something nothing does anywhere — which would make it a rule " +
            "about a port that has quietly stopped being used.");
    }

    private static List<string> BackgroundServices() =>
        [.. Directory
            .EnumerateFiles(
                Path.Combine(ProjectReferenceGraph.RepositoryRoot, "Src", "Presentation"),
                "*BackgroundService.cs",
                SearchOption.AllDirectories)
            .Where(IsProductionSource)];

    private static bool IsProductionSource(string file) =>
        !file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
        && !file.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal);

    private static string Relative(string file) =>
        Path.GetRelativePath(ProjectReferenceGraph.RepositoryRoot, file).Replace('\\', '/');
}
