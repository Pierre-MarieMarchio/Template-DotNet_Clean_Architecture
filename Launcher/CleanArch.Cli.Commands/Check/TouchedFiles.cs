using CleanArch.Cli.Commands.Kernel;

namespace CleanArch.Cli.Commands.Check;

internal static class TouchedFiles
{
    private static readonly string[] _baseCandidates = ["origin/develop", "origin/HEAD", "origin/main", "origin/master", "develop", "main", "master"];

    public static string[] Of(string root, Shell shell)
    {
        if (shell.Capture(Git(root, "rev-parse", "--show-toplevel"), echo: false).ExitCode != ExitCode.Succeeded)
        {
            throw new InvalidOperationException($"check reads what git diff touches, and {root} is not inside a git working tree.");
        }

        string? baseline = PickBase(candidate => shell.Capture(Git(root, "rev-parse", "--verify", "--quiet", candidate + "^{commit}"), echo: false).ExitCode == ExitCode.Succeeded);
        string baseCommit = baseline is null ? "HEAD" : MergeBase(root, shell, baseline);
        shell.Output.WriteLine($"Compared with {Described(baseline, baseCommit)}, the working tree and the files not yet tracked.");
        return
        [
            .. Listed(root, shell, "diff", "--name-only", "--no-renames", "--relative", "-z", baseCommit),
            .. Listed(root, shell, "ls-files", "--others", "--exclude-standard", "-z"),
        ];
    }

    public static string? PickBase(Func<string, bool> exists) => _baseCandidates.FirstOrDefault(exists);

    private static string MergeBase(string root, Shell shell, string baseline)
    {
        CapturedRun run = shell.Capture(Git(root, "merge-base", "HEAD", baseline), echo: false);
        return run.ExitCode == ExitCode.Succeeded ? run.Output.Trim() : baseline;
    }

    private static string Described(string? baseline, string baseCommit) =>
        baseCommit == "HEAD" ? "HEAD" : $"{baseline} at {baseCommit[..Math.Min(8, baseCommit.Length)]}";

    private static string[] Listed(string root, Shell shell, params string[] arguments)
    {
        ExternalCall call = Git(root, arguments);
        shell.Output.WriteLine(call.Line);
        CapturedRun run = shell.Capture(call, echo: false);
        return run.ExitCode == ExitCode.Succeeded
            ? run.Output.TrimEnd('\r', '\n').Split('\0', StringSplitOptions.RemoveEmptyEntries)
            : throw new InvalidOperationException($"'git {string.Join(' ', arguments)}' exited with {run.ExitCode}: {run.Output.Trim()}");
    }

    private static ExternalCall Git(string root, params IReadOnlyList<string> arguments) => new("git", arguments, root);
}
