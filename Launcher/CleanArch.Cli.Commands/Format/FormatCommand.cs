using System.CommandLine;
using System.Text.RegularExpressions;
using CleanArch.Cli.Commands.Hosting;
using CleanArch.Cli.Commands.Kernel;

namespace CleanArch.Cli.Commands.Format;

internal static class FormatCommand
{
    public const string Name = "format";

    public static readonly CommandRegistration Registration = new(Refuse, Create);

    internal static readonly string[] StyleRulesTheBuildLacks = ["IDE0003", "IDE0049"];

    private static readonly string[] _skippedDirectories = ["bin", "obj", "node_modules", "plans"];

    private static readonly string[] _keptDotDirectories = [".github", ".template.config"];

    private static readonly Regex _namingViolation = new(
        @"^(?<file>.+?)\((?<line>\d+),\d+\): (?:error|warning) IDE1006: (?<message>.+?) \[[^\]]+\]\s*$",
        RegexOptions.Multiline | RegexOptions.ExplicitCapture,
        TimeSpan.FromSeconds(5));

    private static string? Refuse(CommandContext context) =>
        context.Project is null
            ? context.RefusedProjectFile?.Message ?? context.OutsideProject
            : null;

    private static Command Create(CommandContext context)
    {
        var command = new Command(Name, "dotnet format over the solution, Markdown saved as UTF-8 without a BOM with LF endings, then the names no formatter can fix.");
        Option<bool> check = new("--check") { Description = "Fail where the command would write instead of writing: dotnet format --verify-no-changes over what the build does not check, and the Markdown." };
        command.Options.Add(check);
        command.SetAction(parseResult => Run(context, parseResult, check));
        return command;
    }

    private static int Run(CommandContext context, ParseResult parseResult, Option<bool> check)
    {
        TextWriter output = parseResult.InvocationConfiguration.Output;
        TextWriter error = parseResult.InvocationConfiguration.Error;
        if (context.Project is not { } project)
        {
            error.WriteLine(context.RefusedProjectFile?.Message ?? context.OutsideProject);
            return CleanArchCommand.Failed;
        }

        return parseResult.GetValue(check)
            ? Check(project, output, error)
            : Fix(project, output, error);
    }

    private static int Fix(LocatedProject project, TextWriter output, TextWriter error)
    {
        string solution = ProjectLayout.Solution(project);
        int formatted = ExternalCommand.Run(Dotnet(project, ["format", solution]), output, error);
        if (formatted != CleanArchCommand.Succeeded)
        {
            return formatted;
        }

        foreach (string file in MarkdownToNormalise(project.Root))
        {
            File.WriteAllBytes(file, NormalisedMarkdown(File.ReadAllBytes(file)));
            output.WriteLine($"{Relative(project.Root, file)}: saved as UTF-8 without a BOM, with LF endings.");
        }

        return RequireNamingStyle(project, solution, output, error);
    }

    private static int Check(LocatedProject project, TextWriter output, TextWriter error)
    {
        string[] arguments = ["format", ProjectLayout.Solution(project), "--diagnostics", .. StyleRulesTheBuildLacks, "--verify-no-changes", "--verbosity", "normal"];
        int verified = ExternalCommand.Run(Dotnet(project, arguments), output, error);
        if (verified != CleanArchCommand.Succeeded)
        {
            return verified;
        }

        string[] markdown = [.. MarkdownToNormalise(project.Root)];
        foreach (string file in markdown)
        {
            error.WriteLine($"{Relative(project.Root, file)}: would be saved as UTF-8 without a BOM, with LF endings.");
        }

        if (markdown.Length == 0)
        {
            return CleanArchCommand.Succeeded;
        }

        error.WriteLine($"{CleanArchCommand.Name} {Name} would write {markdown.Length} Markdown file(s); run it without --check to repair them.");
        return CleanArchCommand.Failed;
    }

    private static int RequireNamingStyle(LocatedProject project, string solution, TextWriter output, TextWriter error)
    {
        ExternalCall call = Dotnet(project, ["format", solution, "--diagnostics", "IDE1006", "--verify-no-changes", "--no-restore"]);
        using var captured = new StringWriter();
        int exit = ExternalCommand.Run(call, output, captured, captured);
        string[] violations = NamingViolations(project.Root, captured.ToString());
        if (violations.Length > 0)
        {
            error.WriteLine("dotnet format cannot rename a symbol, so these names break a naming rule of .editorconfig (IDE1006) and are left as they are:");
            foreach (string violation in violations)
            {
                error.WriteLine("  " + violation);
            }

            error.WriteLine($"Rename each one, with the IDE's rename or by hand, then run {CleanArchCommand.Name} {Name} again.");
            return CleanArchCommand.Failed;
        }

        if (exit != CleanArchCommand.Succeeded)
        {
            error.Write(captured.ToString());
            error.WriteLine($"dotnet format exited with {exit} and named no naming violation: its output is above.");
            return exit;
        }

        return CleanArchCommand.Succeeded;
    }

    private static string[] NamingViolations(string root, string output) =>
        [.. _namingViolation.Matches(output)
            .Select(match => $"{Relative(root, match.Groups["file"].Value)}:{match.Groups["line"].Value}: {match.Groups["message"].Value}")
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

    private static string Relative(string root, string file) => Path.GetRelativePath(root, file).Replace('\\', '/');

    private static ExternalCall Dotnet(LocatedProject project, IReadOnlyList<string> arguments) =>
        new("dotnet", arguments, project.Root);
}
