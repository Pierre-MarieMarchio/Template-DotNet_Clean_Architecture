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
        StartsWithAByteOrderMark(File.ReadAllBytes(Path)).ShouldBeFalse(
            "coverage.minimum is read by a POSIX shell as well as by .NET. A BOM makes the two "
            + "disagree about where the first line starts. Save the file as UTF-8 without BOM.");
    }

    [Fact]
    public void TheFile_StatesOneFloorAndOnly()
    {
        NumericLines().Count.ShouldBe(
            1,
            "the floor is the one bare number in this file; every other line is a comment. Keep one "
            + "number line and prefix every other line with '#'.");
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
            "coverage.minimum promises one floor to CI and to `dotnet cleanarch coverage`.");
    }

    [Fact]
    public void TheReaders_AreSensitive_AndSeeABomAndTellTheFloorFromComments()
    {
        StartsWithAByteOrderMark([0xEF, 0xBB, 0xBF, 0x37]).ShouldBeTrue();
        StartsWithAByteOrderMark([0x37, 0x30]).ShouldBeFalse();
        StartsWithAByteOrderMark([]).ShouldBeFalse();

        NumericLinesOf(["# the floor, in percent", "", "70.5\r", "7o", "# 80"]).ShouldBe(["70.5"]);
        NumericLinesOf(["70", "80"]).Count.ShouldBe(2);
        NumericLinesOf(["# only a comment"]).ShouldBeEmpty();
    }

    private static bool StartsWithAByteOrderMark(byte[] content) =>
        content.Length >= 3 && content[0] == 0xEF && content[1] == 0xBB && content[2] == 0xBF;

    private static List<string> NumericLines() => NumericLinesOf(File.ReadAllLines(Path));

    private static List<string> NumericLinesOf(IEnumerable<string> lines) =>
        [.. lines
            .Select(line => line.Trim('\r', ' ', '\t'))
            .Where(line => line.Length > 0
                && line.All(character => char.IsAsciiDigit(character) || character == '.'))];
}
