namespace CleanArch.Cli.Commands.Coverage;

internal sealed class AssemblyLines
{
    private readonly Dictionary<(string File, int Line), int> _hits = [];
    private readonly Dictionary<(string File, int Line), (int Covered, int Total)> _branches = [];

    public void RecordLine(string file, int line, int hits)
    {
        var key = (file, line);
        _hits[key] = _hits.TryGetValue(key, out int previous) ? Math.Max(previous, hits) : hits;
    }

    public void RecordBranches(string file, int line, int covered, int total)
    {
        var key = (file, line);
        int previousCovered = _branches.TryGetValue(key, out var stored) ? stored.Covered : 0;
        _branches[key] = (Math.Max(previousCovered, covered), total);
    }

    public AssemblyCoverage Measure(string assembly) =>
        new(
            assembly,
            _hits.Values.Count(hits => hits > 0),
            _hits.Count,
            _branches.Values.Sum(branch => branch.Covered),
            _branches.Values.Sum(branch => branch.Total));
}
