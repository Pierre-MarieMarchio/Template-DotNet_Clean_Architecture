using System.Collections.ObjectModel;
using CleanArch.Tooling.Processes;

namespace CleanArch.Create.Setup;

internal sealed class Git(IProcessRunner processes, string root, Func<string, string?> environment)
{
    public const string Init = "git init";
    public const string LongPaths = "git config --global core.longpaths true";

    private static readonly string[] _identity = ["user.name", "user.email"];

    public static bool Repeatable(string command) =>
        command != Init && !command.StartsWith("git config", StringComparison.Ordinal);

    public ProcessResult Run(params string[] arguments) =>
        processes.Run(new ProcessCall("git", root, arguments, ReadOnlyDictionary<string, string>.Empty), null);

    public bool InsideAWorkTree()
    {
        ProcessResult result = Run("rev-parse", "--is-inside-work-tree");
        return result.ExitCode == 0 && result.Output.Trim() == "true";
    }

    public List<string> MissingIdentity() => [.. _identity.Where(setting => !Configured(setting))];

    private bool Configured(string setting)
    {
        ProcessResult result = Run("config", setting);
        if (result.ExitCode == 0 && result.Output.Trim().Length > 0)
        {
            return true;
        }

        string field = setting[(setting.IndexOf('.') + 1)..].ToUpperInvariant();
        return !string.IsNullOrWhiteSpace(environment($"GIT_AUTHOR_{field}")) && !string.IsNullOrWhiteSpace(environment($"GIT_COMMITTER_{field}"));
    }
}
