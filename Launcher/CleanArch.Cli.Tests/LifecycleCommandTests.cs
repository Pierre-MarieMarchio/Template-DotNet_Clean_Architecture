using Shouldly;
using Xunit;

namespace CleanArch.Cli.Tests;

[Collection(ProcessWideState.Name)]
public sealed class LifecycleCommandTests : IDisposable
{
    private readonly StringWriter _output = new();
    private readonly StringWriter _error = new();
    private readonly List<ExternalCall> _calls = [];
    private int _exit = CleanArchCommand.Succeeded;

    public LifecycleCommandTests()
    {
        ExternalCommand.Handler = (call, _, _) =>
        {
            _calls.Add(call);
            return _exit;
        };
    }

    [Fact]
    public void ComposeDown_RunsAtTheProjectRoot_FromASubfolder()
    {
        using var project = new SampleProject(SampleProject.Full);

        Run(["compose", "down"], project.Folder("Src")).ShouldBe(CleanArchCommand.Succeeded);

        _calls.Single().Arguments.ShouldBe(["compose", "down"]);
        _calls.Single().WorkingDirectory.ShouldBe(project.Root);
    }

    [Fact]
    public void Compose_Alone_ShowsItsSubcommands()
    {
        using var project = new SampleProject(SampleProject.Full);

        Run(["compose"], project.Root).ShouldBe(CleanArchCommand.Succeeded);

        _output.ToString().ShouldContain("dotnet cleanarch compose");
        _output.ToString().ShouldContain("up");
        _output.ToString().ShouldContain("down");
        _calls.ShouldBeEmpty();
        _error.ToString().ShouldBeEmpty();
    }

    [Fact]
    public void Sonar_StartsSonarQube_ThenScansWithTheUnitTestsAlone()
    {
        using var project = WithTests(SampleProject.WithOptions("local", sonar: true));

        Run(["sonar"], project.Root).ShouldBe(CleanArchCommand.Succeeded);

        _calls.Count.ShouldBe(2);
        _calls[0].FileName.ShouldBe("docker");
        _calls[0].WorkingDirectory.ShouldBe(project.Root);
        _calls[0].Arguments.ShouldBe(["compose", "--profile", "sonar", "up", "--detach", "--wait", "sonarqube"]);
        _calls[1].FileName.ShouldBe("docker");
        _calls[1].Arguments.Take(9).ShouldBe(["compose", "--profile", "sonar", "run", "--rm", "--build", "sonar-scanner", "/bin/sh", "-c"]);
        string script = _calls[1].Arguments[9];
        script.ShouldContain($"dotnet test --project \"{Path.Combine("Tests", "Unit", "AS.Unit.Tests.csproj").Replace('\\', '/')}\"");
        script.ShouldContain("dotnet restore Acme.Shop.slnx --artifacts-path artifacts/sonar");
        script.ShouldNotContain("Integration");
        _error.ToString().ShouldBeEmpty();
    }

    [Fact]
    public void Sonar_ForwardsTheExitCodeOfTheSonarQubeService()
    {
        using var project = WithTests(SampleProject.WithOptions("local", sonar: true));
        _exit = 8;

        Run(["sonar"], project.Root).ShouldBe(8);

        _calls.Single().Arguments.ShouldBe(["compose", "--profile", "sonar", "up", "--detach", "--wait", "sonarqube"]);
    }

    public void Dispose()
    {
        ExternalCommand.Handler = null;
        _output.Dispose();
        _error.Dispose();
    }

    private int Run(string[] arguments, string workingDirectory) =>
        CleanArchCommand.Run(arguments, workingDirectory, _output, _error);

    private static SampleProject WithTests(string json)
    {
        var project = new SampleProject(json);
        string unit = project.Folder(Path.Combine("Tests", "Unit"));
        File.WriteAllText(
            Path.Combine(unit, "AS.Unit.Tests.csproj"),
            "<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><IsTestProject>true</IsTestProject></PropertyGroup></Project>");
        string integration = project.Folder(Path.Combine("Tests", "Integration"));
        File.WriteAllText(
            Path.Combine(integration, "AS.Integration.Tests.csproj"),
            "<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><IsTestProject>true</IsTestProject></PropertyGroup><ItemGroup><PackageReference Include=\"Testcontainers\" /></ItemGroup></Project>");
        return project;
    }
}
