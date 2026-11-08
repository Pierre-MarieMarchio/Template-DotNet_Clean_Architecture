using CleanArch.Cli.Commands.Check;
using Shouldly;
using Xunit;

namespace CleanArch.Cli.Tests;

public sealed class CheckCommandTests : IDisposable
{
    private const string _architecture = "tests/Architecture/AS.Architecture.Tests/Acme.Shop.Architecture.Tests.csproj";
    private const string _domain = "src/Domain/AS.Domain/Acme.Shop.Domain.csproj";
    private const string _domainTests = "tests/Domain/AS.Domain.UnitTests/Acme.Shop.Domain.UnitTests.csproj";
    private const string _integration = "tests/Integration/AS.Integration/Acme.Shop.Integration.csproj";

    private static readonly SolutionProject[] _layered =
    [
        new("src/Domain/P.Domain.Core/X.Domain.Core.csproj", "src/Domain/P.Domain.Core", [], false),
        new("src/Domain/P.Domain/X.Domain.csproj", "src/Domain/P.Domain", ["src/Domain/P.Domain.Core/X.Domain.Core.csproj"], false),
        new("src/Application/P.Application/X.Application.csproj", "src/Application/P.Application", ["src/Domain/P.Domain/X.Domain.csproj"], false),
        new("src/Infrastructure/P.Infrastructure/X.Infrastructure.csproj", "src/Infrastructure/P.Infrastructure", ["src/Application/P.Application/X.Application.csproj"], false),
        new("tests/Domain/P.Domain.UnitTests/X.Domain.UnitTests.csproj", "tests/Domain/P.Domain.UnitTests", ["src/Domain/P.Domain/X.Domain.csproj"], true),
        new("tests/Domain/P.Domain.Core.UnitTests/X.Domain.Core.UnitTests.csproj", "tests/Domain/P.Domain.Core.UnitTests", ["src/Domain/P.Domain.Core/X.Domain.Core.csproj"], true),
        new("tests/Integration/P.Integration/X.Integration.csproj", "tests/Integration/P.Integration", ["src/Infrastructure/P.Infrastructure/X.Infrastructure.csproj"], false),
    ];

    private static readonly string[] _everyLayeredProject =
        ["X.Application.csproj", "X.Domain.Core.UnitTests.csproj", "X.Domain.Core.csproj", "X.Domain.UnitTests.csproj", "X.Domain.csproj", "X.Infrastructure.csproj", "X.Integration.csproj"];

    private readonly StringWriter _output = new() { NewLine = "\n" };
    private readonly StringWriter _error = new();
    private readonly List<string> _dotnetCalls = [];
    private readonly Dictionary<string, string> _filters = new(StringComparer.Ordinal);

    public static TheoryData<string, string[], bool, string[]> Scopes => new()
    {
        { "a file of a project touches it and every project that references it, however far", ["src/Domain/P.Domain/Features/Notes/Note.cs"], false,
            ["X.Application.csproj", "X.Domain.UnitTests.csproj", "X.Domain.csproj", "X.Infrastructure.csproj", "X.Integration.csproj"] },
        { "the nearest project owns a file, not the project whose folder name it starts with", ["src/Domain/P.Domain.Core/Id.cs"], false, _everyLayeredProject },
        { "a folder whose name merely starts with a project's folder name belongs to no project", ["src/Domain/P.DomainExtras/Id.cs"], false, [] },
        { "a test project touches nothing but itself", ["tests/Domain/P.Domain.UnitTests/NoteTests.cs"], false, ["X.Domain.UnitTests.csproj"] },
        { "a file outside every project touches none", ["docs/README.md", "tools/Tasks.cs"], false, [] },
        { "a file is placed by its path alone, so a deleted one still touches its project", ["src/Application/P.Application/Gone.cs"], false,
            ["X.Application.csproj", "X.Infrastructure.csproj", "X.Integration.csproj"] },
        { "Directory.Build.props sets the build for every project", ["Directory.Build.props"], true, _everyLayeredProject },
        { "a root .editorconfig sets the build for every project", ["src/Domain/P.Domain/Note.cs", ".editorconfig"], true, _everyLayeredProject },
        { "a project file sets the build for every project", ["src/Domain/P.Domain/X.Domain.csproj"], true, _everyLayeredProject },
        { "an .editorconfig above no project does not", ["launcher/.editorconfig"], false, [] },
        { "an .editorconfig inside a project does", ["src/Domain/P.Domain/Generated/.editorconfig"], true, _everyLayeredProject },
        { "a Markdown file in the root does not", ["README.md"], false, [] },
    };

    [Theory]
    [MemberData(nameof(Scopes))]
    public void TheScope_IsTheProjectsTheTouchedFilesBelongTo_AndEveryProjectReferencingThem(string rule, string[] changed, bool buildWide, string[] projects)
    {
        CheckScope scope = ProjectGraph.ScopeOf(changed, _layered);

        scope.BuildWide.ShouldBe(buildWide, rule);
        scope.Projects.Select(path => path[(path.LastIndexOf('/') + 1)..]).Order(StringComparer.Ordinal).ShouldBe(projects, ignoreOrder: false, customMessage: rule);
    }

    [Fact]
    public void TheBase_IsTheFirstCandidateThatExists_OriginDevelopFirst_AndHeadWhenNoneDoes()
    {
        TouchedFiles.PickBase(candidate => candidate is "origin/main" or "develop").ShouldBe("origin/main");
        TouchedFiles.PickBase(candidate => candidate is "origin/develop" or "origin/HEAD").ShouldBe("origin/develop");
        TouchedFiles.PickBase(_ => false).ShouldBeNull();
    }

    [Fact]
    public void ATestProjectThatNeedsDocker_IsBuiltAndNamedNotRun_AndOneOnWindowsAlone_IsSkippedOffWindows()
    {
        const string desktopTests = "tests/Presentation/P.Desktop.UnitTests/X.Desktop.UnitTests.csproj";
        const string domainTests = "tests/Domain/P.Domain.UnitTests/X.Domain.UnitTests.csproj";
        const string architecture = "tests/Architecture/P.Architecture.Tests/X.Architecture.Tests.csproj";
        const string endToEnd = "tests/EndToEnd/P.EndToEnd.Tests/X.EndToEnd.Tests.csproj";
        SolutionProject[] graph =
        [
            new("src/Presentation/P.Desktop/X.Desktop.csproj", "src/Presentation/P.Desktop", [], false, WindowsOnly: true),
            new(desktopTests, "tests/Presentation/P.Desktop.UnitTests", ["src/Presentation/P.Desktop/X.Desktop.csproj"], true, WindowsOnly: true),
            new(domainTests, "tests/Domain/P.Domain.UnitTests", [], true),
            new(architecture, "tests/Architecture/P.Architecture.Tests", [], true),
            new(endToEnd, "tests/EndToEnd/P.EndToEnd.Tests", [], false),
        ];
        string[] built = [.. graph.Select(project => project.Path)];

        (string[] testedOff, string[] skippedOff, string[] notRunOff) = CheckPlan.TestedProjects(built, graph, architecture, onWindows: false);
        (string[] testedOn, string[] skippedOn, string[] notRunOn) = CheckPlan.TestedProjects(built, graph, architecture, onWindows: true);
        (_, _, string[] notRunWhenAbsent) = CheckPlan.TestedProjects([.. built.Where(path => path != endToEnd)], graph, architecture, onWindows: true);

        skippedOff.ShouldBe([desktopTests]);
        testedOff.ShouldBe([domainTests, architecture], ignoreOrder: true);
        skippedOn.ShouldBeEmpty();
        testedOn.ShouldBe([desktopTests, domainTests, architecture], ignoreOrder: true);
        notRunOff.ShouldBe([endToEnd]);
        notRunOn.ShouldBe([endToEnd]);
        notRunWhenAbsent.ShouldBeEmpty();
        graph.Where(project => project.NeedsDocker).Select(project => project.Path).ShouldBe([endToEnd]);
    }

    [Fact]
    public void OnThisRepository_AFileOfTheDomain_RunsItsUnitTestsAndTheArchitectureTests_AndBuildsWithoutRunningWhatNeedsDocker()
    {
        LocatedProject project = ProjectLocator.Find(Repository.Root).ShouldNotBeNull();

        CheckPlan plan = CheckPlan.Of(project, ["src/Domain/AT.Domain/Features/Touched.cs"], onWindows: false);

        plan.Scope.BuildWide.ShouldBeFalse();
        plan.Tested.ShouldContain("tests/Domain/AT.Domain.UnitTests/AppTemplate.Domain.UnitTests.csproj");
        plan.Tested.ShouldContain("tests/Architecture/AT.Architecture.Tests/AppTemplate.Architecture.Tests.csproj");
        plan.Tested.ShouldNotContain(path => path.StartsWith("tests/Integration/", StringComparison.Ordinal));
        plan.NotRun.ShouldContain("tests/Integration/AT.Api.IntegrationTests/AppTemplate.Api.IntegrationTests.csproj");
        plan.Built.ShouldBe([.. plan.Built.Union(plan.Tested).Union(plan.NotRun)], ignoreOrder: true);
    }

    [Fact]
    public void Check_WhenNothingIsTouched_LaunchesNothingAndPasses()
    {
        using SampleProject project = SampleTree();

        Run(project, touched: "", untracked: "").ShouldBe(CleanArchCommand.Succeeded);

        _output.ToString().ShouldContain("Compared with origin/develop at 0123abcd, the working tree and the files not yet tracked.");
        _output.ToString().ShouldContain(CheckCommand.NothingTouched);
        _dotnetCalls.ShouldBeEmpty();
        _error.ToString().ShouldBeEmpty();
    }

    [Fact]
    public void Check_FormatsTheTouchedCSharpFiles_BuildsWhatTheyReach_AndDeletesItsFiltersWhenTheBuildFails()
    {
        using SampleProject project = SampleTree();

        Run(project, touched: "src/Domain/AS.Domain/A.cs\0Src/Domain/AS.Domain/Gone.cs\0docs/a.md\0", untracked: "tools/New.cs\0", buildExit: 3).ShouldBe(3);

        _dotnetCalls.ShouldBe(
        [
            "format Acme.Shop.slnx --diagnostics IDE0003 IDE0049 --verify-no-changes --include src/Domain/AS.Domain/A.cs",
            "build artifacts/test/check-build.slnf --configuration Debug",
        ]);
        string built = _filters["artifacts/test/check-build.slnf"];
        foreach (string reached in new[] { _domain, _domainTests, _integration, _architecture })
        {
            built.ShouldContain(reached);
        }

        Directory.EnumerateFiles(Path.Combine(project.Root, "artifacts", "test")).ShouldBeEmpty();
    }

    [Fact]
    public void Check_WhenAFileSetsTheBuildForEveryProject_BuildsTheSolutionAndRunsOnlyItsDockerFreeTests()
    {
        using SampleProject project = SampleTree();

        Run(project, touched: "Directory.Build.props\0", untracked: "").ShouldBe(CleanArchCommand.Succeeded);

        _dotnetCalls.ShouldBe(
        [
            "build Acme.Shop.slnx --configuration Debug",
            "test --solution artifacts/test/docker-free.slnf --configuration Debug --no-build",
        ]);
        string tested = _filters["artifacts/test/docker-free.slnf"];
        tested.ShouldContain(_domainTests);
        tested.ShouldContain(_architecture);
        tested.ShouldNotContain(_integration);
        _output.ToString().ShouldContain("Built, not run tests/Integration/AS.Integration: it needs Docker.");
        Directory.EnumerateFiles(Path.Combine(project.Root, "artifacts", "test")).ShouldBeEmpty();
    }

    public void Dispose()
    {
        _output.Dispose();
        _error.Dispose();
    }

    private static SampleProject SampleTree()
    {
        var project = new SampleProject(SampleProject.Minimal);
        File.WriteAllText(Path.Combine(project.Root, SampleSolution.Name + ".slnx"), $"""
            <Solution>
              <Project Path="{_domain}" />
              <Project Path="{_domainTests}" />
              <Project Path="{_integration}" />
              <Project Path="{_architecture}" />
            </Solution>
            """);
        WriteProject(project, _domain, "<Project Sdk=\"Microsoft.NET.Sdk\" />");
        WriteProject(project, _domainTests, $"""
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup><IsTestProject>true</IsTestProject></PropertyGroup>
              <ItemGroup><ProjectReference Include="..\..\..\{_domain}" /></ItemGroup>
            </Project>
            """);
        WriteProject(project, _integration, SampleSolution.IntegrationProject);
        WriteProject(project, _architecture, SampleSolution.UnitProject);
        File.WriteAllText(Path.Combine(project.Root, "src", "Domain", "AS.Domain", "A.cs"), "class A;\n");
        return project;
    }

    private static void WriteProject(SampleProject project, string path, string content)
    {
        project.Folder(Path.GetDirectoryName(path)!);
        File.WriteAllText(Path.Combine(project.Root, path), content);
    }

    private int Run(SampleProject project, string touched, string untracked, int buildExit = 0) =>
        BuiltInCommands.Run(["check"], project.Root, _output, _error, new FakeProcesses((call, output, _) => call.FileName == "git"
            ? Git(call.Arguments, output, touched, untracked)
            : Dotnet(call, buildExit)));

    private static int Git(IReadOnlyList<string> arguments, TextWriter output, string touched, string untracked)
    {
        string? written = arguments[0] switch
        {
            "merge-base" => "0123abcdef456789",
            "diff" => touched,
            "ls-files" => untracked,
            _ => null,
        };
        if (written is not null)
        {
            output.WriteLine(written);
        }

        return CleanArchCommand.Succeeded;
    }

    private int Dotnet(ExternalCall call, int buildExit)
    {
        _dotnetCalls.Add(string.Join(' ', call.Arguments));
        foreach (string filter in call.Arguments.Where(argument => argument.EndsWith(".slnf", StringComparison.Ordinal)))
        {
            _filters[filter] = File.ReadAllText(Path.Combine(call.WorkingDirectory, filter));
        }

        return call.Arguments[0] == "build" ? buildExit : CleanArchCommand.Succeeded;
    }
}
