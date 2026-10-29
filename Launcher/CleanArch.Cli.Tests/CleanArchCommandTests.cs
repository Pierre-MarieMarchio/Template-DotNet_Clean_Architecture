using Shouldly;
using Xunit;

namespace CleanArch.Cli.Tests;

public sealed class CleanArchCommandTests : IDisposable
{
    private readonly StringWriter _output = new();
    private readonly StringWriter _error = new();

    [Fact]
    public void Help_ListsTheOptionsTheToolOffers()
    {
        Run(["--help"], Path.GetTempPath()).ShouldBe(CleanArchCommand.Succeeded);

        _output.ToString().ShouldContain("--version");
        _output.ToString().ShouldContain("--help");
        _error.ToString().ShouldBeEmpty();
    }

    [Fact]
    public void Version_PrintsTheVersionOfTheTool()
    {
        Run(["--version"], Path.GetTempPath()).ShouldBe(CleanArchCommand.Succeeded);

        _output.ToString().Trim().ShouldNotBeNullOrWhiteSpace();
        _error.ToString().ShouldBeEmpty();
    }

    [Fact]
    public void Run_NamesTheProject_InsideOne()
    {
        using var project = new SampleProject(SampleProject.Full);

        Run([], project.Folder("Src")).ShouldBe(CleanArchCommand.Succeeded);

        _output.ToString().ShouldContain("Acme.Shop");
        _output.ToString().ShouldContain("1.2.3");
        _output.ToString().ShouldContain(project.Root);
    }

    [Fact]
    public void Run_SaysItIsNotInsideAProject_Outside()
    {
        string folder = Directory.CreateTempSubdirectory("cleanarch-cli-").FullName;
        try
        {
            Run([], folder).ShouldBe(CleanArchCommand.Failed);
        }
        finally
        {
            Directory.Delete(folder);
        }

        _error.ToString().ShouldStartWith("Not inside a CleanArch project");
        _error.ToString().ShouldContain(ProjectLocator.FileName);
        _output.ToString().ShouldBeEmpty();
    }

    [Fact]
    public void Run_ReportsAProjectFileItCannotRead()
    {
        using var project = new SampleProject("{ not json");

        Run([], project.Root).ShouldBe(CleanArchCommand.Failed);

        _error.ToString().ShouldContain(ProjectLocator.FileName);
    }

    public void Dispose()
    {
        _output.Dispose();
        _error.Dispose();
    }

    private int Run(string[] arguments, string workingDirectory) =>
        CleanArchCommand.Run(arguments, workingDirectory, _output, _error);
}
