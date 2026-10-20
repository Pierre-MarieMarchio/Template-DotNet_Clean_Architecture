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
        @"AppTemplate\.(?:Application|Infrastructure)\.Auth\b|AppTemplate\.Api\.Features\.Auth\b",
        RegexOptions.None,
        TimeSpan.FromSeconds(5));

    private static readonly Regex _leaseDependency = new(
        @"\bILeaderLease\s+[a-z_][A-Za-z0-9_]*",
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
            10,
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
        _authenticationReference.IsMatch("using AppTemplate.Api.Features.Maintenance.Jobs;").ShouldBeFalse();
    }

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
