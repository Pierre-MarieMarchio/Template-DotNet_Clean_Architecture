using Shouldly;
using Xunit;

namespace CleanArch.Create.Tests;

public sealed class RelayedOutputTests
{
    private static readonly string[] _generated =
    [
        "The template \"Clean Architecture API\" was created successfully.",
        string.Empty,
        "Processing post-creation actions...",
        "Description: Bootstrap the project before anything else.",
        "Manual instructions: In the project folder, run dotnet run Tools/Tasks.cs bootstrap and commit what it writes.",
        string.Empty,
    ];

    [Fact]
    public void AnInstructionToRunWhatTheLauncherRuns_IsLeftOut_WithItsHeading()
    {
        Relayed("dotnet run Tools/Tasks.cs bootstrap").ShouldBe([_generated[0], string.Empty, string.Empty]);
    }

    [Fact]
    public void WithNothingRunByTheLauncher_EveryLineIsRelayed()
    {
        Relayed(null).ShouldBe(_generated);
    }

    [Fact]
    public void AnotherInstruction_IsRelayed_WithItsHeading()
    {
        Relayed("pnpm install").ShouldBe(_generated);
    }

    private static List<string> Relayed(string? runByTheLauncher)
    {
        var written = new List<string>();
        var relayed = new RelayedOutput(written.Add, runByTheLauncher);
        foreach (string line in _generated)
        {
            relayed.Line(line);
        }

        relayed.Flush();
        return written;
    }
}
