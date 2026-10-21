#:property UseAppHost=false

using System.Text;
using System.Text.RegularExpressions;

return FeatureEdits.Run(args);

internal static class FeatureEdits
{
    private const string Usage =
        """
        Usage: dotnet run .template.config/acceptance/ApplyFeatureEdits.cs <new-feature output> <root>

        Applies the hand edits a new-feature run printed to the generated project at <root>: the
        lines each edit prints, placed beside the neighbours a person would choose, with the usings
        they need, then the feature's rows in docs/README.md and docs/reference/api.md. It reads the
        output and nothing else, so an edit the output no longer prints in its usual form fails here
        before it misleads a reader.

        Exit code 0 once every edit is applied; 2, writing nothing, when the output holds an edit
        given as prose only (the first owner feature of a project generated without one); 1 on any
        other problem.
        """;

    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(2);

    private static readonly UTF8Encoding Utf8WithBom = new(encoderShouldEmitUTF8Identifier: true);

    private static readonly Regex EditHeader = new(@"^ {1,2}(\d+)\. (.*)$", RegexOptions.None, Timeout);

    private static readonly Regex Identifier = new(@"\b[A-Z][A-Za-z0-9_]*\b", RegexOptions.None, Timeout);

    private static readonly Regex TypeDeclaration = new(
        @"^\s*(?:(?:public|internal|private|protected|sealed|static|abstract|partial|readonly)\s+)*(?:class|interface|record|enum|struct)\s+([A-Z][A-Za-z0-9_]*)",
        RegexOptions.Multiline,
        Timeout);

    private static readonly Regex FileNamespace = new(@"^namespace\s+([A-Za-z0-9_.]+);", RegexOptions.Multiline, Timeout);

    private static readonly HashSet<string> SkippedFolders = new(StringComparer.Ordinal) { "bin", "obj", ".git", "TestResults" };

    private sealed record Edit(int Number, string Path, string Header, List<string> Code);

    internal static int Run(string[] arguments)
    {
        if (arguments is not [string output, string root] || arguments.Contains("--help"))
        {
            Console.WriteLine(Usage);
            return arguments.Contains("--help") ? 0 : 1;
        }

        try
        {
            return Apply(File.ReadAllLines(output), Path.GetFullPath(root));
        }
        catch (InvalidOperationException error)
        {
            Console.Error.WriteLine(error.Message);
            return 1;
        }
    }

    private static int Apply(string[] output, string root)
    {
        List<Edit> edits = Edits(output);

        if (edits.Count == 0)
        {
            throw new InvalidOperationException("The output prints no numbered edit: new-feature no longer prints its edits in the form this reads.");
        }

        Edit[] prose = [.. edits.Where(edit => Strategy(edit) is null)];

        if (prose.Length > 0)
        {
            Console.WriteLine(
                $"Edit(s) {string.Join(", ", prose.Select(edit => edit.Number))} name code the output does not print "
                + "(the first owner feature of a project generated without one), so no edit is applied.");
            return 2;
        }

        var declarations = Declarations(root);

        foreach (Edit edit in edits)
        {
            Strategy(edit)!(root, edit, declarations);
            Console.WriteLine($"Applied edit {edit.Number}: {edit.Path}");
        }

        DocumentRow(root, output, "docs/README.md", "## Features", "Then its row under ## Features in docs/README.md:", required: true);
        DocumentRow(root, output, "docs/reference/api.md", "### Resources", "And the row of its refusal under ### Resources in docs/reference/api.md", required: false);

        return 0;
    }

    private static List<Edit> Edits(string[] output)
    {
        var edits = new List<Edit>();
        Edit? current = null;

        foreach (string line in output)
        {
            if (EditHeader.Match(line) is { Success: true } header)
            {
                string text = header.Groups[2].Value;
                int colon = text.IndexOf(": ", StringComparison.Ordinal);
                string path = (colon < 0 ? text : text[..colon]).Trim().TrimEnd(':');

                current = new Edit(int.Parse(header.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture), path, text, []);
                edits.Add(current);
            }
            else if (current is not null && line.Length > 0 && !line.StartsWith(' '))
            {
                break;
            }
            else if (current is not null && line.StartsWith("       ", StringComparison.Ordinal) && Code(line) is { Length: > 0 } code)
            {
                current.Code.Add(code);
            }
            else if (current is not null && line.StartsWith("     ", StringComparison.Ordinal))
            {
                current = current with { Header = current.Header + " " + line.Trim() };
                edits[^1] = current;
            }
        }

        return edits;
    }

    private static string Code(string line)
    {
        string trimmed = line.Trim();
        int comment = trimmed.IndexOf("//", StringComparison.Ordinal);

        return (comment < 0 ? trimmed : trimmed[..comment]).TrimEnd();
    }

    private static Action<string, Edit, Dictionary<string, List<string>>>? Strategy(Edit edit)
    {
        if (edit.Header.StartsWith("Nothing to edit:", StringComparison.Ordinal))
        {
            return (_, _, _) => { };
        }

        return Path.GetFileName(edit.Path) switch
        {
            "AppDbContext.cs" when edit.Code.Count == 3 => Context,
            "ApplicationModule.cs" when edit.Code.Count > 0 => ApplicationModule,
            "PersistenceModule.cs" when edit.Code.Count > 0 => PersistenceModule,
            "Program.cs" when Inline(edit, @"builder\.Services\.Add\w+\(\);") is not null => ProgramFile,
            "HostComposition.cs" when edit.Code.Count == 0 && Inline(edit, @"services\.Add\w+\(\);") is not null => HostComposition,
            "ApplicationModuleTests.cs" when Inline(edit, @"services\.Add\w+\(\);") is not null => ApplicationModuleTests,
            _ => null,
        };
    }

    private static string? Inline(Edit edit, string pattern) =>
        Regex.Match(edit.Header, pattern, RegexOptions.None, Timeout) is { Success: true } match ? match.Value : null;

    private static void Context(string root, Edit edit, Dictionary<string, List<string>> declarations)
    {
        List<string> lines = Read(root, edit.Path);

        InsertBefore(lines, "    public const string PlatformSchema", ["    " + edit.Code[0], string.Empty]);
        InsertBefore(lines, "    internal DbSet<IdempotencyRecord>", ["    " + edit.Code[1], string.Empty]);
        InsertBefore(lines, "        modelBuilder.ApplyConfiguration(new IdempotencyRecordConfiguration());", ["        " + edit.Code[2], string.Empty]);

        Write(root, edit.Path, WithUsings(lines, edit.Code, declarations));
    }

    private static void ApplicationModule(string root, Edit edit, Dictionary<string, List<string>> declarations)
    {
        string method = Required(Regex.Match(edit.Header, @"an (Add\w+)\(\)", RegexOptions.None, Timeout), edit);
        List<string> lines = Read(root, edit.Path);

        InsertBefore(
            lines,
            "    private static void AddFeature(",
            [
                $"    public static IServiceCollection {method}(this IServiceCollection services)",
                "    {",
                "        ArgumentNullException.ThrowIfNull(services);",
                string.Empty,
                .. edit.Code.Select(code => "        " + code),
                string.Empty,
                "        return services;",
                "    }",
                string.Empty,
            ]);

        Write(root, edit.Path, WithUsings(lines, edit.Code, declarations));
    }

    private static void PersistenceModule(string root, Edit edit, Dictionary<string, List<string>> declarations)
    {
        string method = Required(Regex.Match(edit.Header, @"an (Add\w+)\(services\)", RegexOptions.None, Timeout), edit);
        List<string> lines = Read(root, edit.Path);

        InsertBefore(lines, "        AddIdempotencyFeature(services, configuration);", [$"        {method}(services);"]);
        InsertBefore(
            lines,
            "    private static void AddIdempotencyFeature(",
            [
                $"    private static void {method}(IServiceCollection services)",
                "    {",
                .. edit.Code.Select(code => "        " + code),
                "    }",
                string.Empty,
            ]);

        Write(root, edit.Path, WithUsings(lines, edit.Code, declarations));
    }

    private static void ProgramFile(string root, Edit edit, Dictionary<string, List<string>> declarations)
    {
        string call = Inline(edit, @"builder\.Services\.Add\w+\(\);")!;
        List<string> lines = Read(root, edit.Path);

        InsertBefore(lines, "builder.Services.AddPurgeExpiredIdempotencyKeys();", [call, string.Empty]);

        Write(root, edit.Path, WithUsings(lines, [call], declarations, ApplicationNamespace(lines, edit)));
    }

    private static void HostComposition(string root, Edit edit, Dictionary<string, List<string>> declarations)
    {
        string call = Inline(edit, @"services\.Add\w+\(\);")!;
        List<string> lines = Read(root, edit.Path);
        int composed = 0;

        for (int index = 0; index < lines.Count; index++)
        {
            if (lines[index].Trim() != "services.AddPurgeExpiredIdempotencyKeys();" || !ComposesPersistence(lines, index))
            {
                continue;
            }

            lines.Insert(index, "        " + call);
            index++;
            composed++;
        }

        if (composed == 0)
        {
            throw new InvalidOperationException($"{edit.Path}: no composition both purges idempotency keys and adds the persistence module, so edit {edit.Number} has no place.");
        }

        Write(root, edit.Path, WithUsings(lines, [call], declarations, ApplicationNamespace(lines, edit)));
    }

    private static bool ComposesPersistence(List<string> lines, int index)
    {
        for (int next = index; next < lines.Count && lines[next] != "    }"; next++)
        {
            if (lines[next].Contains("services.AddPersistenceModule(", StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    private static void ApplicationModuleTests(string root, Edit edit, Dictionary<string, List<string>> declarations)
    {
        string call = Inline(edit, @"services\.Add\w+\(\);")!;
        List<string> lines = Read(root, edit.Path);
        int method = lines.FindIndex(line => line.Contains(" ComposeEveryFeature()", StringComparison.Ordinal));
        int end = method < 0 ? -1 : lines.FindIndex(method, line => line == "        return services;");

        if (end < 0)
        {
            throw new InvalidOperationException($"{edit.Path}: no ComposeEveryFeature returning its services, so edit {edit.Number} has no place.");
        }

        lines.Insert(end - 1, "        " + call);

        Write(root, edit.Path, WithUsings(lines, [call], declarations));
    }

    private static string? ApplicationNamespace(List<string> lines, Edit edit)
    {
        Match named = Regex.Match(edit.Header, @"with using ([A-Za-z0-9_.]+);", RegexOptions.None, Timeout);

        return named.Success && !lines.Contains($"using {named.Groups[1].Value};") ? named.Groups[1].Value : null;
    }

    private static string Required(Match match, Edit edit) =>
        match.Success
            ? match.Groups[1].Value
            : throw new InvalidOperationException($"Edit {edit.Number} no longer names its method in the form this reads: {edit.Header}");

    private static void InsertBefore(List<string> lines, string anchor, string[] inserted)
    {
        int index = lines.FindIndex(line => line.StartsWith(anchor, StringComparison.Ordinal));

        if (index < 0)
        {
            throw new InvalidOperationException($"No line starts with '{anchor}', the neighbour an edit is placed beside.");
        }

        lines.InsertRange(index, inserted);
    }

    private static List<string> WithUsings(
        List<string> lines,
        IEnumerable<string> code,
        Dictionary<string, List<string>> declarations,
        string? alsoNeeded = null)
    {
        string? own = lines.Select(line => FileNamespace.Match(line)).FirstOrDefault(match => match.Success)?.Groups[1].Value;
        var needed = new SortedSet<string>(StringComparer.Ordinal);

        if (alsoNeeded is not null)
        {
            needed.Add(alsoNeeded);
        }

        foreach (Match name in code.SelectMany(text => Identifier.Matches(text)))
        {
            if (declarations.TryGetValue(name.Value, out List<string>? namespaces) && namespaces.Count == 1)
            {
                needed.Add(namespaces[0]);
            }
        }

        foreach (string space in needed.Where(space => space != own && !lines.Contains($"using {space};")))
        {
            int usings = lines.FindLastIndex(line => line.StartsWith("using ", StringComparison.Ordinal));
            int position = lines.FindIndex(line => line.StartsWith("using ", StringComparison.Ordinal)
                && string.CompareOrdinal(line[6..].TrimEnd(';'), space) > 0);

            lines.Insert(position >= 0 ? position : usings + 1, $"using {space};");
        }

        return lines;
    }

    private static Dictionary<string, List<string>> Declarations(string root)
    {
        var declarations = new Dictionary<string, List<string>>(StringComparer.Ordinal);

        foreach (string file in SourceFiles(root))
        {
            string text = File.ReadAllText(file);

            if (FileNamespace.Match(text) is not { Success: true } space)
            {
                continue;
            }

            foreach (Match type in TypeDeclaration.Matches(text))
            {
                if (!declarations.TryGetValue(type.Groups[1].Value, out List<string>? namespaces))
                {
                    declarations[type.Groups[1].Value] = namespaces = [];
                }

                if (!namespaces.Contains(space.Groups[1].Value))
                {
                    namespaces.Add(space.Groups[1].Value);
                }
            }
        }

        return declarations;
    }

    private static IEnumerable<string> SourceFiles(string folder)
    {
        foreach (string file in Directory.EnumerateFiles(folder, "*.cs"))
        {
            yield return file;
        }

        foreach (string child in Directory.EnumerateDirectories(folder).Where(child => !SkippedFolders.Contains(Path.GetFileName(child))))
        {
            foreach (string file in SourceFiles(child))
            {
                yield return file;
            }
        }
    }

    private static void DocumentRow(string root, string[] output, string page, string heading, string announcement, bool required)
    {
        int announced = Array.FindIndex(output, line => line.StartsWith(announcement, StringComparison.Ordinal));

        if (announced < 0 && !required)
        {
            return;
        }

        if (announced < 0 || announced + 1 >= output.Length || !output[announced + 1].TrimStart().StartsWith('|'))
        {
            throw new InvalidOperationException($"The output no longer announces its row in {page} as '{announcement}' followed by the row.");
        }

        List<string> lines = Read(root, page);
        int section = lines.FindIndex(line => line == heading);
        int table = section < 0 ? -1 : lines.FindIndex(section, line => line.StartsWith("|---", StringComparison.Ordinal));

        if (table < 0)
        {
            throw new InvalidOperationException($"{page} has no table under '{heading}' to take the feature's row.");
        }

        int last = table;

        while (last + 1 < lines.Count && lines[last + 1].StartsWith('|'))
        {
            last++;
        }

        lines.Insert(last + 1, output[announced + 1].Trim());
        Write(root, page, lines);
        Console.WriteLine($"Added the feature's row to {page}");
    }

    private static List<string> Read(string root, string relative)
    {
        string path = Path.Combine(root, relative);

        if (!File.Exists(path))
        {
            throw new InvalidOperationException($"{relative} does not exist, so the edit the output names there cannot be applied.");
        }

        return [.. File.ReadAllText(path).TrimStart('﻿').TrimEnd('\n').Split('\n')];
    }

    private static void Write(string root, string relative, List<string> lines) =>
        File.WriteAllText(Path.Combine(root, relative), string.Join('\n', lines) + "\n", Utf8WithBom);
}
