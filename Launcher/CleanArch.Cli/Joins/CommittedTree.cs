namespace CleanArch.Cli.Joins;

internal static class CommittedTree
{
    private const string _why = "presentation-join moves the API's jobs into the worker, which only `git restore` undoes, and ";

    public static void Require(JoinProcesses processes, string root, IReadOnlyList<string> paths, IReadOnlyList<string> newFolders)
    {
        if (processes.Quietly(Git(root, "rev-parse", "--is-inside-work-tree")).ExitCode != ExitCode.Succeeded)
        {
            throw new JoinRefusedException(
                _why + "this project is no git repository, so no file was changed: run `git init`, commit the project, then run the task again.");
        }

        CapturedRun status = processes.Quietly(Git(root, ["status", "--porcelain=v1", "--untracked-files=all", "--", .. paths]));
        string[] changed = [.. status.Output.Split('\n', StringSplitOptions.RemoveEmptyEntries).Where(line => !IsNewIn(line, newFolders))];

        if (status.ExitCode != ExitCode.Succeeded || changed.Length > 0)
        {
            throw new JoinRefusedException(
                _why + "these files it would move, edit or remove differ from what git last committed, so no file was changed: "
                + "commit or restore them, then run the task again."
                + Environment.NewLine + (status.ExitCode != ExitCode.Succeeded ? status.Output.TrimEnd() : string.Join(Environment.NewLine, changed)));
        }
    }

    private static bool IsNewIn(string line, IReadOnlyList<string> folders) =>
        line.StartsWith("?? ", StringComparison.Ordinal) && folders.Any(folder => line[3..].StartsWith(folder, StringComparison.Ordinal));

    private static ExternalCall Git(string root, params IReadOnlyList<string> arguments) => new("git", arguments, root);
}
