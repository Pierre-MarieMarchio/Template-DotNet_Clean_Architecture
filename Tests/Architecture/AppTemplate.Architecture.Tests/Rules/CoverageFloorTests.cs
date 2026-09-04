using System.Globalization;
using AppTemplate.Architecture.Tests.Fixtures;
using Shouldly;
using Xunit;

namespace AppTemplate.Architecture.Tests.Rules;

/// <summary>
/// <c>coverage.minimum</c> is read twice by two languages — by a POSIX shell in
/// <c>.github/workflows/ci.yml</c> and by <c>Tools/Tasks.cs</c> — and its own header promises the
/// gate a developer runs is the gate CI runs. Nothing held it to that.
/// <para>
/// One byte was enough to break it. A UTF-8 BOM in front of the leading <c>#</c> means GNU grep
/// does not see a comment line, so a filter written as "every line that is not a comment" hands the
/// floor over with the whole comment glued to it, while <c>File.ReadLines</c> strips the BOM and
/// reads the number. The file then said 85 to one reader and a sentence to the other.
/// </para>
/// </summary>
public sealed class CoverageFloorTests
{
    private static string Path => System.IO.Path.Combine(
        ProjectReferenceGraph.RepositoryRoot, "coverage.minimum");

    /// <summary>
    /// No BOM. It is not a C# file — <c>.editorconfig</c> requires one there and this is read by
    /// <c>tr</c>, <c>grep</c> and <c>head</c>, none of which skip it.
    /// </summary>
    [Fact]
    public void TheFile_CarriesNoByteOrderMark()
    {
        var first = File.ReadAllBytes(Path).Take(3).ToArray();

        first.ShouldNotBe(
            [0xEF, 0xBB, 0xBF],
            "coverage.minimum is read by a POSIX shell as well as by .NET. A BOM makes the two "
            + "disagree about where the first line starts.");
    }

    /// <summary>
    /// Exactly one, so "the first line that is a number" and "the number in this file" are the same
    /// sentence. Two would make the readers' agreement an accident of ordering.
    /// </summary>
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

    /// <summary>
    /// The two definitions, side by side: what a shell pipeline picking bare-number lines finds, and
    /// what <c>Tools/Tasks.cs</c> finds by parsing each trimmed line. They have to be the same
    /// string, because CI enforces the first and a contributor's terminal enforces the second.
    /// </summary>
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
