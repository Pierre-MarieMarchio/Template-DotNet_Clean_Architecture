using System.CommandLine;
using System.Text;

namespace CleanArch.Cli;

internal static class FormatCommand
{
    public const string Name = "format";

    public static readonly CommandRegistration Registration = new(Refuse, Create);

    private static readonly string[] _skippedDirectories = ["bin", "obj", "node_modules", "plans"];

    private static readonly string[] _keptDotDirectories = [".github", ".template.config"];

    private static string? Refuse(CommandContext context) =>
        context.Project is null
            ? context.RefusedProjectFile?.Message ?? context.OutsideProject
            : null;

    private static Command Create(CommandContext context)
    {
        var command = new Command(Name, "Repair the tree at the project root: UTF-8 BOM rules, LF endings, usings ordering.");
        Option<bool> check = new("--check") { Description = "Fail where the command would write instead of writing." };
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

        bool asCheck = parseResult.GetValue(check);
        int toWrite = 0;
        foreach (string file in RepairableFiles(project.Root))
        {
            byte[] content = File.ReadAllBytes(file);
            bool csharp = file.EndsWith(".cs", StringComparison.Ordinal);
            byte[] repaired = Repaired(content, csharp, out bool usingsOrdered);
            if (repaired.AsSpan().SequenceEqual(content))
            {
                continue;
            }

            string relative = Path.GetRelativePath(project.Root, file).Replace('\\', '/');
            string saved = $"saved as UTF-8 {(csharp ? "with" : "without")} a BOM, with LF endings{(usingsOrdered ? ", usings ordered, System first" : "")}";
            if (asCheck)
            {
                error.WriteLine($"{relative}: would be {saved}.");
                toWrite++;
            }
            else
            {
                File.WriteAllBytes(file, repaired);
                output.WriteLine($"{relative}: {saved}.");
            }
        }

        if (asCheck && toWrite > 0)
        {
            error.WriteLine($"{CleanArchCommand.Name} {Name} would write {toWrite} file(s); run it without --check to repair them.");
            return CleanArchCommand.Failed;
        }

        return CleanArchCommand.Succeeded;
    }

    private static IEnumerable<string> RepairableFiles(string root) =>
        TextFiles(root, "*.cs").Concat(TextFiles(root, "*.md")).Order(StringComparer.Ordinal);

    private static IEnumerable<string> TextFiles(string directory, string pattern) =>
        Directory.EnumerateFiles(directory, pattern)
            .Concat(Directory.EnumerateDirectories(directory)
                .Where(child => Path.GetFileName(child) is var name
                    && !_skippedDirectories.Contains(name, StringComparer.Ordinal)
                    && (!name.StartsWith('.') || _keptDotDirectories.Contains(name, StringComparer.Ordinal)))
                .SelectMany(child => TextFiles(child, pattern)));

    private static byte[] Repaired(byte[] content, bool csharp, out bool usingsOrdered)
    {
        bool hasBom = content is [0xEF, 0xBB, 0xBF, ..];
        byte[] withoutCr = WithoutCarriageReturns(content.AsSpan(hasBom ? 3 : 0));
        usingsOrdered = false;
        byte[] body = withoutCr;
        if (csharp)
        {
            string source = Encoding.UTF8.GetString(withoutCr);
            string ordered = OrderedUsings(source);
            usingsOrdered = !string.Equals(ordered, source, StringComparison.Ordinal);
            body = Encoding.UTF8.GetBytes(ordered);
        }

        return csharp ? [0xEF, 0xBB, 0xBF, .. body] : body;
    }

    private static byte[] WithoutCarriageReturns(ReadOnlySpan<byte> text)
    {
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

    private static string OrderedUsings(string source)
    {
        string[] lines = source.Split('\n');
        int end = 0;
        while (end < lines.Length && (IsUsing(lines[end]) || lines[end].Length == 0))
        {
            end++;
        }

        List<string> usings = [.. lines[..end].Where(IsUsing).Select(line => line.Trim())];
        if (usings.Count == 0)
        {
            return source;
        }

        usings.Sort(CompareUsings);
        int trailingBlanks = 0;
        for (int index = end - 1; index >= 0 && lines[index].Length == 0; index--)
        {
            trailingBlanks++;
        }

        var rebuilt = new List<string>(lines.Length);
        rebuilt.AddRange(usings);
        for (int index = 0; index < trailingBlanks; index++)
        {
            rebuilt.Add(string.Empty);
        }

        rebuilt.AddRange(lines[end..]);
        return string.Join('\n', rebuilt);
    }

    private static bool IsUsing(string line)
    {
        string trimmed = line.Trim();
        return trimmed.StartsWith("using ", StringComparison.Ordinal) && trimmed.EndsWith(';');
    }

    private static int CompareUsings(string left, string right)
    {
        string a = NameOf(left);
        string b = NameOf(right);
        bool aSystem = IsSystem(a);
        bool bSystem = IsSystem(b);
        if (aSystem != bSystem)
        {
            return aSystem ? -1 : 1;
        }

        return string.CompareOrdinal(a, b);
    }

    private static string NameOf(string usingDirective) => usingDirective["using ".Length..^1].Trim();

    private static bool IsSystem(string name) =>
        name == "System" || name.StartsWith("System.", StringComparison.Ordinal);
}
