using System.Text.RegularExpressions;
using AppTemplate.Architecture.Tests.Fixtures;
using Shouldly;
using Xunit;

namespace AppTemplate.Architecture.Tests.Rules;

public sealed class BackgroundWorkTests
{
    private static readonly Regex _plainScope = new(
        @"\.Create(?:Async)?Scope\(",
        RegexOptions.None,
        TimeSpan.FromSeconds(5));

    private static readonly Regex _authenticationReference = new(
        @"AppTemplate\.(?:Application|Infrastructure)\.Auth\b|AppTemplate\.\w+\.Features\.Auth\b",
        RegexOptions.None,
        TimeSpan.FromSeconds(5));

    private static readonly Regex _leaseDependency = new(
        @"\bILeaderLease\s+[a-z_][A-Za-z0-9_]*",
        RegexOptions.None,
        TimeSpan.FromSeconds(5));

    private static readonly Regex _backgroundJobsCall = new(
        @"\.AddBackgroundJobs\(",
        RegexOptions.None,
        TimeSpan.FromSeconds(5));

    private static readonly Regex _commentLine = new(
        @"^\s*(?://|///).*$",
        RegexOptions.Multiline,
        TimeSpan.FromSeconds(5));

    private static string CodeOf(string file) =>
        _commentLine.Replace(File.ReadAllText(file), string.Empty);

    [Fact]
    public void NoBackgroundService_TakesTheLeaderLease()
    {
        var services = BackgroundServices();

        services.Count.ShouldBeGreaterThanOrEqualTo(
            1,
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

    [Fact]
    public void TheJobs_RunInOneHost()
    {
        var hosts = ProjectReferenceGraph.Hosts
            .Where(host => SourcesOf(host).Any(file => _backgroundJobsCall.IsMatch(CodeOf(file))))
            .Select(host => host.Name)
            .Order(StringComparer.Ordinal)
            .ToList();

        hosts.ShouldBe(
            [JobsHost().Name],
            "The jobs run in one host, the one that holds Features/BackgroundJobs/Jobs: a second host that "
            + "calls AddBackgroundJobs runs every pass twice, and the lease is then the only thing between them.");
    }

    [Fact]
    public void TheJobsRule_IsSensitive_AndSeesTheCallItCounts()
    {
        _backgroundJobsCall.IsMatch("builder.Services.AddBackgroundJobs(builder.Configuration);").ShouldBeTrue();
        _backgroundJobsCall.IsMatch("public static IServiceCollection AddBackgroundJobs(").ShouldBeFalse();
    }

    [Fact]
    public void TheLeaseRule_IsSensitive_AndSeesTheDependencyItForbids()
    {
        _leaseDependency.IsMatch("internal sealed class Loop(ILeaderLease lease) : BackgroundService")
            .ShouldBeTrue();

        _leaseDependency.IsMatch("internal sealed class Loop(IServiceScopeFactory factory)")
            .ShouldBeFalse();

        _commentLine.Replace("/// Neither loop takes ILeaderLease lease, and that is deliberate.", string.Empty)
            .ShouldBeEmpty();
    }

    //#if (reminders || files)
    [Fact]
    public void TheLeaderLease_IsTakenByAUseCase()
    {
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
    //#endif

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
                1,
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

    [Fact]
    public void TheBackgroundComposition_NamesNoAuthentication()
    {
        var files = BackgroundCompositionFiles();

        files.Count.ShouldBeGreaterThanOrEqualTo(
            9,
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
        _authenticationReference.IsMatch("using AppTemplate.Application.Auth.Features.Sessions.UseCases;")
            .ShouldBeTrue();
        _authenticationReference.IsMatch("using AppTemplate.Infrastructure.Auth;").ShouldBeTrue();
        _authenticationReference.IsMatch("using AppTemplate.Api.Features.Auth.Jobs;").ShouldBeTrue();
        _authenticationReference.IsMatch("using AppTemplate.Worker.Features.Auth.Jobs;").ShouldBeTrue();
        _authenticationReference.IsMatch("using AppTemplate.Api.Features.Maintenance.Jobs;").ShouldBeFalse();
    }

    private static ProjectNode JobsHost()
    {
        var holders = ProjectReferenceGraph.Hosts
            .Where(host => File.Exists(Path.Combine(
                ProjectReferenceGraph.RootOf(host), "Features", "BackgroundJobs", "Jobs", "BackgroundJobsModule.cs")))
            .ToList();

        holders.Count.ShouldBe(
            1,
            "Exactly one host holds Features/BackgroundJobs/Jobs/BackgroundJobsModule.cs; the background rules read "
            + "the jobs of that host. Found: " + string.Join(", ", holders.Select(host => host.Name)));

        return holders[0];
    }

    private static IEnumerable<string> SourcesOf(ProjectNode project) =>
        Directory
            .EnumerateFiles(ProjectReferenceGraph.RootOf(project), "*.cs", SearchOption.AllDirectories)
            .Where(IsProductionSource);

    private static List<string> BackgroundCompositionFiles()
    {
        string host = ProjectReferenceGraph.RootOf(JobsHost());

        var folders = new List<string> { Path.Combine(host, "Common", "Jobs") };

        folders.AddRange(Directory
            .EnumerateDirectories(Path.Combine(host, "Features"))
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
