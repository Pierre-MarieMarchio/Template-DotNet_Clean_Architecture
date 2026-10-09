using System.Text.RegularExpressions;
using AppTemplate.Architecture.Tests.Fixtures;
using Shouldly;
using Xunit;

namespace AppTemplate.Architecture.Tests.Rules;

/// <summary>
/// Where a guarantee about running once across the fleet is allowed to live.
/// <para>
/// Read from the source tree rather than from metadata, because this project deliberately does not
/// reference <c>AppTemplate.Api</c> — it composes the modules itself, so that the rules here stay
/// about the modules rather than about one host. The background services are therefore invisible to
/// reflection from here, and the text is what is left.
/// </para>
/// </summary>
public sealed class BackgroundWorkTests
{
    /// <summary>
    /// A scope opened the framework's way. <c>CreateBackgroundScope</c> does not match, because the
    /// name before <c>Scope</c> has to be <c>Create</c> or <c>CreateAsync</c> itself.
    /// </summary>
    private static readonly Regex _plainScope = new(
        @"\.Create(?:Async)?Scope\(",
        RegexOptions.None,
        TimeSpan.FromSeconds(5));

    /// <summary>
    /// A namespace from the authentication layers of the application or the infrastructure, or from
    /// the API's own authentication feature — a name in code, not a word in a sentence.
    /// </summary>
    private static readonly Regex _authenticationReference = new(
        @"AppTemplate\.(?:Application|Infrastructure)\.Auth\b|AppTemplate\.Api\.Features\.Auth\b",
        RegexOptions.None,
        TimeSpan.FromSeconds(5));

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
            2,
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
        // Every project of the layer, not the one this rule was written against: a lease-taking use
        // case in another of them is the same fact, and a walk that missed it would report that
        // nothing takes the lease.
        var consumers = ProjectReferenceGraph
            .ProjectsInLayer("Application")
            .Select(project => Path.Combine(ProjectReferenceGraph.RootOf(project), "Features"))
            .Where(Directory.Exists)
            .SelectMany(features => Directory.EnumerateFiles(features, "*UseCase.cs", SearchOption.AllDirectories))
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

    /// <summary>
    /// A scope opened any other way is, to the caller identity, a request that happens to carry no
    /// principal: <c>ICurrentUser</c> answers null instead of refusing, and a use case that wrongly
    /// asks who is calling runs on as an anonymous caller. <c>CreateBackgroundScope</c> is what marks
    /// a scope as a job's, so a service that skipped it compiles, runs and quietly loses the guard.
    /// </summary>
    [Fact]
    public void EveryBackgroundService_OpensItsScopeAsABackgroundScope()
    {
        var services = BackgroundServices();

        var offenders = services
            .Where(service => _plainScope.IsMatch(CodeOf(service)))
            .Select(Relative)
            .Order(StringComparer.Ordinal)
            .ToList();

        offenders.ShouldBeEmpty(
            "A background service opened a scope with CreateScope or CreateAsyncScope. Use " +
            "CreateBackgroundScope, so that the scope has no caller and its writes record no user.");

        services
            .Count(service => CodeOf(service).Contains("CreateBackgroundScope(", StringComparison.Ordinal))
            .ShouldBeGreaterThanOrEqualTo(
                2,
                "Fewer background services open a background scope than this template ships, so " +
                "the rule above is reading the wrong tree and passing for the wrong reason.");
    }

    [Fact]
    public void TheScopeRule_IsSensitive_AndSeesTheCallsItForbids()
    {
        _plainScope.IsMatch("await using var scope = scopeFactory.CreateAsyncScope();").ShouldBeTrue();
        _plainScope.IsMatch("using var scope = provider.CreateScope();").ShouldBeTrue();
        _plainScope.IsMatch("await using var scope = scopeFactory.CreateBackgroundScope();").ShouldBeFalse();
    }

    /// <summary>
    /// What a project built without authentication must be able to delete without editing the
    /// background composition: the composition, the options it binds and the jobs' own folders name
    /// nothing from authentication. Its refresh-token purge joins the maintenance timer from
    /// <c>Features/Auth/Jobs</c>, which is the one folder that does.
    /// </summary>
    [Fact]
    public void TheBackgroundComposition_NamesNoAuthentication()
    {
        var files = BackgroundCompositionFiles();

        files.Count.ShouldBeGreaterThanOrEqualTo(
            14,
            "Far fewer files were found in the background composition than this template ships, so " +
            "this rule is reading the wrong tree and passing for the wrong reason.");

        var offenders = files
            .Where(file => _authenticationReference.IsMatch(CodeOf(file)))
            .Select(Relative)
            .Order(StringComparer.Ordinal)
            .ToList();

        offenders.ShouldBeEmpty(
            "The background composition names authentication, so removing authentication would mean " +
            "editing it. Let authentication register what it contributes — Features/Auth/Jobs does " +
            "this for the refresh-token purge — and keep the composition ignorant of it.");
    }

    [Fact]
    public void TheAuthenticationRule_IsSensitive_AndSeesWhatItForbids()
    {
        _authenticationReference.IsMatch("using AppTemplate.Application.Auth.Features.Auth.UseCases;")
            .ShouldBeTrue();
        _authenticationReference.IsMatch("using AppTemplate.Infrastructure.Auth;").ShouldBeTrue();
        _authenticationReference.IsMatch("using AppTemplate.Api.Features.Auth.Jobs;").ShouldBeTrue();
        _authenticationReference.IsMatch("using AppTemplate.Api.Features.Maintenance.Jobs;").ShouldBeFalse();
    }

    /// <summary>
    /// The shared job mechanisms and every feature's <c>Jobs/</c> folder but authentication's own —
    /// which includes the composition feature.
    /// </summary>
    private static List<string> BackgroundCompositionFiles()
    {
        string api = ProjectReferenceGraph.RootOf(ProjectReferenceGraph.Project("AppTemplate.Api"));

        var folders = new List<string> { Path.Combine(api, "Common", "Jobs") };

        folders.AddRange(Directory
            .EnumerateDirectories(Path.Combine(api, "Features"))
            .Where(feature => !string.Equals(Path.GetFileName(feature), "Auth", StringComparison.Ordinal))
            .Select(feature => Path.Combine(feature, "Jobs"))
            .Where(Directory.Exists));

        return
        [
            .. folders
                .SelectMany(folder => Directory.EnumerateFiles(folder, "*.cs", SearchOption.AllDirectories))
                .Where(IsProductionSource)
                .Order(StringComparer.Ordinal),
        ];
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
