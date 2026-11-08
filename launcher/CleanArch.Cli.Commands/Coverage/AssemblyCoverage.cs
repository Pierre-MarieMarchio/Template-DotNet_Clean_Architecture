namespace CleanArch.Cli.Commands.Coverage;

internal sealed record AssemblyCoverage(string Assembly, int LinesCovered, int LinesValid, int BranchesCovered, int BranchesValid)
{
    public double LineRate => Percent(LinesCovered, LinesValid);

    public double BranchRate => Percent(BranchesCovered, BranchesValid);

    public static double Percent(int covered, int total) => total != 0 ? 100.0 * covered / total : 100.0;
}
