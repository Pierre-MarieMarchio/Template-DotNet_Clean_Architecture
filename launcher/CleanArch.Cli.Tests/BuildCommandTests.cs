using Shouldly;
using Xunit;

namespace CleanArch.Cli.Tests;

public sealed class BuildCommandTests : IDisposable
{
    private readonly StringWriter _output = new();
    private readonly StringWriter _error = new();
    private readonly FakeDotnet _dotnet = new();

    [Fact]
    public void Build_RunsAtTheRootFromASubfolder()
    {
        using var project = SampleSolution.WithSolution();

        Run(["build"], project.Folder("src")).ShouldBe(CleanArchCommand.Succeeded);

        _dotnet.Calls.ShouldBe(["build Acme.Shop.slnx --configuration Debug"]);
    }

    public void Dispose()
    {
        _output.Dispose();
        _error.Dispose();
    }

    private int Run(string[] arguments, string workingDirectory) =>
        BuiltInCommands.Run(arguments, workingDirectory, _output, _error, _dotnet);
}
