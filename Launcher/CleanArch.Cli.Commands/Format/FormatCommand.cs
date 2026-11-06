using System.CommandLine;
using System.Text.RegularExpressions;
using CleanArch.Cli.Commands.Hosting;
using CleanArch.Cli.Commands.Kernel;

namespace CleanArch.Cli.Commands.Format;

internal static class FormatCommand
{
    public const string Name = "format";

    public static readonly CommandRegistration Registration = new(ProjectCommand.Refusal, Create);

    private static readonly string[] _skippedDirectories = ["bin", "obj", "node_modules", "plans"];

    private static readonly string[] _keptDotDirectories = [".github", ".template.config"];

    private static readonly Regex _namingViolation = new(
        @"^(?<file>.+?)\((?<line>\d+),\d+\): (?:error|warning) IDE1006: (?<message>.+?) \[[^\]]+\]\s*$",
        RegexOptions.Multiline | RegexOptions.ExplicitCapture,
        TimeSpan.FromSeconds(5));

    private static Command Create(CommandContext context)
    {
        var command = new Command(Name, "dotnet format over the solution, Markdown saved as UTF-8 without a BOM with LF endings, then the names no formatter can fix.");
        Option<bool> check = new("--check") { Description = "Fail where the command would write instead of writing: dotnet format --verify-no-changes over what the build does not check, and the Markdown." };
        command.Options.Add(check);
        if (context.Project is { } project)
        {
            command.SetAction(parseResult => Run(project, context.Shell(parseResult), parseResult.GetValue(check)));
        }

        return command;
    }

    private static int Run(LocatedProject project, Shell shell, bool checking) => checking ? Check(project, shell) : Fix(project, shell);

    private static int Fix(LocatedProject project, Shell shell)
    {
        string solution = ProjectLayout.Solution(project);
        int formatted = shell.Run(Dotnet.Call(project.Root, "format", solution));
        if (formatted != ExitCode.Succeeded)
        {
            return formatted;
        }

        foreach (string file in MarkdownToNormalise(project.Root))
        {
            File.WriteAllBytes(file, NormalisedMarkdown(File.ReadAllBytes(file)));
            shell.Output.WriteLine($"{ProjectPath.Relative(project.Root, file)}: saved as UTF-8 without a BOM, with LF endings.");
        }

        return RequireNamingStyle(project, solution, shell);
    }

    private static int Check(LocatedProject project, Shell shell)
    {
        int verified = shell.Run(Dotnet.StyleCheck(project.Root, ProjectLayout.Solution(project), "--verbosity", "normal"));
        if (verified != ExitCode.Succeeded)
        {
            return verified;
        }

        string[] markdown = [.. MarkdownToNormalise(project.Root)];
        foreach (string file in markdown)
        {
            shell.Error.WriteLine($"{ProjectPath.Relative(project.Root, file)}: would be saved as UTF-8 without a BOM, with LF endings.");
        }

        return markdown.Length == 0
            ? ExitCode.Succeeded
            : shell.Fail($"{Tool.Name} {Name} would write {markdown.Length} Markdown file(s); run it without --check to repair them.");
    }

    private static int RequireNamingStyle(LocatedProject project, string solution, Shell shell)
    {
        using var captured = new StringWriter();
        int exit = shell.Run(Dotnet.Call(project.Root, "format", solution, "--diagnostics", "IDE1006", "--verify-no-changes", "--no-restore"), captured, captured);
        string[] violations = NamingViolations(project.Root, captured.ToString());
        if (violations.Length > 0)
        {
            shell.Error.WriteLine("dotnet format cannot rename a symbol, so these names break a naming rule of .editorconfig (IDE1006) and are left as they are:");
            foreach (string violation in violations)
            {
                shell.Error.WriteLine("  " + violation);
            }

            return shell.Fail($"Rename each one, with the IDE's rename or by hand, then run {Tool.Name} {Name} again.");
        }

        if (exit != ExitCode.Succeeded)
        {
            shell.Error.Write(captured.ToString());
            shell.Error.WriteLine($"dotnet format exited with {exit} and named no naming violation: its output is above.");
        }

        return exit;
    }

    private static string[] NamingViolations(string root, string output) =>
        [.. _namingViolation.Matches(output)
            .Select(match => $"{ProjectPath.Relative(root, match.Groups["file"].Value)}:{match.Groups["line"].Value}: {match.Groups["message"].Value}")
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)];

    private static IEnumerable<string> MarkdownToNormalise(string root) =>
        MarkdownFiles(root)
            .Where(file => File.ReadAllBytes(file) is var content && !NormalisedMarkdown(content).AsSpan().SequenceEqual(content))
            .Order(StringComparer.Ordinal);

    private static IEnumerable<string> MarkdownFiles(string directory) =>
        Directory.EnumerateFiles(directory, "*.md")
            .Concat(Directory.EnumerateDirectories(directory)
                .Where(child => Path.GetFileName(child) is var name
                    && !_skippedDirectories.Contains(name, StringComparer.Ordinal)
                    && (!name.StartsWith('.') || _keptDotDirectories.Contains(name, StringComparer.Ordinal)))
                .SelectMany(MarkdownFiles));

    private static byte[] NormalisedMarkdown(byte[] content)
    {
        ReadOnlySpan<byte> text = content.AsSpan(content is [0xEF, 0xBB, 0xBF, ..] ? 3 : 0);
        var normalised = new List<byte>(text.Length);
        for (int index = 0; index < text.Length; index++)
        {
            if (text[index] != (byte)'\r')
            {
                normalised.Add(text[index]);
            }
            else if (index + 1 == text.Length || text[index + 1] != (byte)'\n')
            {
                normalised.Add((byte)'\n');
            }
        }

        return [.. normalised];
    }
}
