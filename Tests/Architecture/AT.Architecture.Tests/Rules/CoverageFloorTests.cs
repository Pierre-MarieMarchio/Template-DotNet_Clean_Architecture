using System.Globalization;
using AppTemplate.Architecture.Tests.Fixtures;
using Shouldly;
using Xunit;

namespace AppTemplate.Architecture.Tests.Rules;

public sealed class CoverageFloorTests
{
    private static string Path => System.IO.Path.Combine(
        ProjectReferenceGraph.RepositoryRoot, "coverage.minimum");

    [Fact]
    public void TheFile_CarriesNoByteOrderMark()
    {
        byte[] first = File.ReadAllBytes(Path).Take(3).ToArray();

        first.ShouldNotBe(
            [0xEF, 0xBB, 0xBF],
            "coverage.minimum is read by a POSIX shell as well as by .NET. A BOM makes the two "
            + "disagree about where the first line starts.");
    }

    [Fact]
    public void TheFile_StatesOneFloorAndOnly()
    {
        NumericLines().Count.ShouldBe(
            1,
            "the floor is the one bare number in this file; every other line is a comment.");
    }

    [Fact]
    public void TheFloor_IsAPercentageWorthEnforcing()
    {
        double floor = double.Parse(NumericLines().Single(), CultureInfo.InvariantCulture);

        floor.ShouldBeInRange(
            1,
            100,
            "a floor at zero enforces nothing and one above a hundred can never be met.");
    }

    [Fact]
    public void BothReaders_FindTheSameFloor()
    {
        string asAShellWouldRead = NumericLines()[0];

        string? asTasksReads = File
            .ReadLines(Path)
            .Select(line => line.Trim())
            .FirstOrDefault(line => double.TryParse(line, CultureInfo.InvariantCulture, out _));

        asTasksReads.ShouldBe(
            asAShellWouldRead,
            "coverage.minimum promises one floor to CI and to `dotnet run Tools/Tasks.cs coverage`.");
    }

    private static List<string> NumericLines() =>
        [.. File
            .ReadAllLines(Path)
            .Select(line => line.Trim('\r', ' ', '\t'))
            .Where(line => line.Length > 0
                && line.All(character => char.IsAsciiDigit(character) || character == '.')) ];
}
