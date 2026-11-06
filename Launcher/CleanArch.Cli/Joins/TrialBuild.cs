using System.Text.RegularExpressions;
using CleanArch.Tooling;

namespace CleanArch.Cli.Joins;

internal static partial class TrialBuild
{
    public static void Run(JoinProcesses processes, LocatedProject project, PendingWrites writes, IReadOnlyList<FileMove> moves)
    {
        string trial = Directory.CreateTempSubdirectory("cleanarch-worker-").FullName;
        try
        {
            ProjectTree.Copy(project.Root, trial);
            writes.WriteTo(trial, [.. moves.Select(move => move.From)]);
            Build(processes, project, trial, [.. moves.Select(move => move.To)]);
        }
        finally
        {
            Directory.Delete(trial, recursive: true);
        }
    }

    private static void Build(JoinProcesses processes, LocatedProject project, string trial, string[] moved)
    {
        string name = project.File.Name;
        string workerTests = Path.Combine(
            trial, "Tests", "Presentation", ProjectNames.FolderOf(name, JobMoves.Worker + ".UnitTests"), $"{name}.{JobMoves.Worker}.UnitTests.csproj");

        foreach (string target in (string[])[Path.Combine(trial, ProjectLayout.Solution(project)), workerTests])
        {
            CapturedRun build = processes.Echoed(Dotnet.Call(trial, "build", target));
            if (build.ExitCode != ExitCode.Succeeded)
            {
                throw new JoinRefusedException(Failure(project, trial, target, moved, build.Output));
            }
        }
    }

    private static string Failure(LocatedProject project, string trial, string target, string[] moved, string output)
    {
        string[] missing = Unreachable(trial, ApiTypes(Path.Combine(trial, ProjectLayout.Api(project))), moved, output);

        return missing.Length > 0
            ? "Built in a copy of the project with the jobs in the worker, these name what the worker cannot reach, a type or a namespace only the API declares, "
              + "so no file was changed: " + string.Join("; ", missing) + ". Move each such type to the application layer, or into the job's own folder, "
              + "commit, then run the task again."
            : $"Built in a copy of the project with the jobs in the worker, {Path.GetFileName(target)} does not build, so no file was changed:"
              + Environment.NewLine + output.TrimEnd();
    }

    private static string[] Unreachable(string trial, string[] apiTypes, string[] moved, string output) =>
        [.. UnreachableName().Matches(output)
            .Select(match => Named(trial, match, apiTypes, moved))
            .Distinct(StringComparer.Ordinal)];

    private static string Named(string trial, Match match, string[] apiTypes, string[] moved)
    {
        string file = ProjectPath.Relative(trial, match.Groups["file"].Value.Trim());
        string name = match.Groups["space"].Success ? $"{match.Groups["space"].Value}.{match.Groups["name"].Value}" : match.Groups["name"].Value;
        string[] named = moved.Contains(file, StringComparer.Ordinal) ? NamedApiTypes(Path.Combine(trial, file), apiTypes) : [];

        return $"{file} names {name}" + (named.Length > 0 ? $", and with it {string.Join(", ", named)}" : string.Empty);
    }

    private static string[] ApiTypes(string api) =>
        [.. Directory.EnumerateFiles(api, "*.cs", SearchOption.AllDirectories)
            .SelectMany(file => DeclaredType().Matches(File.ReadAllText(file)).Select(match => match.Groups["type"].Value))
            .Distinct(StringComparer.Ordinal)];

    private static string[] NamedApiTypes(string file, string[] apiTypes)
    {
        string code = File.ReadAllText(file);
        return [.. apiTypes.Where(type => Regex.IsMatch(code, $@"\b{Regex.Escape(type)}\b", RegexOptions.None, TimeSpan.FromSeconds(2))).Order(StringComparer.Ordinal)];
    }

    [GeneratedRegex(
        @"^\s*(?<file>[^\s(][^(\r\n]*)\(\d+,\d+\): error (?:CS0246|CS0103|CS0234): The (?:type or namespace )?name '(?<name>[^']+)'(?: does not exist in the namespace '(?<space>[^']+)')?",
        RegexOptions.Multiline,
        matchTimeoutMilliseconds: 2000)]
    private static partial Regex UnreachableName();

    [GeneratedRegex(
        @"^\s*(?:(?:public|internal|private|protected|static|sealed|abstract|partial|readonly|file)\s+)*(?:class|record|struct|interface|enum)\s+(?<type>[A-Za-z_]\w*)",
        RegexOptions.Multiline,
        matchTimeoutMilliseconds: 2000)]
    private static partial Regex DeclaredType();
}
