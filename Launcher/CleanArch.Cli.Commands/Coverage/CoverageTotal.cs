namespace CleanArch.Cli.Commands.Coverage;

internal sealed record CoverageTotal(int LinesCovered, int LinesValid, int BranchesCovered, int BranchesValid)
{
    public double LineRate => AssemblyCoverage.Percent(LinesCovered, LinesValid);

    public double BranchRate => AssemblyCoverage.Percent(BranchesCovered, BranchesValid);

    public static CoverageTotal Of(IReadOnlyList<AssemblyCoverage> assemblies) =>
        new(
            assemblies.Sum(row => row.LinesCovered),
            assemblies.Sum(row => row.LinesValid),
            assemblies.Sum(row => row.BranchesCovered),
            assemblies.Sum(row => row.BranchesValid));
}
