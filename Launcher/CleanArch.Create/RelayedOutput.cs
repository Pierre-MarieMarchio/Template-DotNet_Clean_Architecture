namespace CleanArch.Create;

public sealed class RelayedOutput
{
    private const string _postActions = "Processing post-creation actions...";
    private const string _description = "Description:";
    private const string _instructions = "Manual instructions:";

    private readonly Action<string> _write;
    private readonly string? _runByTheLauncher;
    private readonly List<string> _held = [];
    private bool _left;

    public RelayedOutput(Action<string> write, string? runByTheLauncher)
    {
        _write = write;
        _runByTheLauncher = runByTheLauncher;
    }

    public void Line(string line)
    {
        if (_runByTheLauncher is null)
        {
            _write(line);
            return;
        }

        if (line.StartsWith(_postActions, StringComparison.Ordinal) || line.StartsWith(_description, StringComparison.Ordinal))
        {
            _held.Add(line);
            return;
        }

        if (line.StartsWith(_instructions, StringComparison.Ordinal) && line.Contains(_runByTheLauncher, StringComparison.Ordinal))
        {
            _held.RemoveAll(held => held.StartsWith(_description, StringComparison.Ordinal));
            _left = true;
            return;
        }

        Flush();
        _write(line);
    }

    public void Flush()
    {
        bool onlyTheHeading = _left && _held.All(held => held.StartsWith(_postActions, StringComparison.Ordinal));
        foreach (string held in onlyTheHeading ? [] : _held)
        {
            _write(held);
        }

        _held.Clear();
    }
}
