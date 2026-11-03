#:property UseAppHost=false

using System.Text;
using System.Text.RegularExpressions;

return FeatureEdits.Run(args);

internal static class FeatureEdits
{
    private const string _usage =
        """
        Usage: dotnet run .template.config/acceptance/ApplyFeatureEdits.cs <new-feature or new-use-case output> <root>

        Applies the hand edits a new-feature run printed to the generated project at <root>: the
        lines each edit prints, placed beside the neighbours a person would choose, with the usings
        they need, then the feature's rows in docs/README.md and docs/reference/api.md and its section of the
        route table under ## Routes. It reads the
        output and nothing else, so an edit the output no longer prints in its usual form fails here
        before it misleads a reader. Given a new-use-case run, it makes the HTTP edit that run printed:
        the usings, the ISender the controller lacks, the action before the controller's closing brace,
        the request and response records it prints, and the action's row in its feature's route table.

        Exit code 0 once every edit is applied; 2, writing nothing, when the output holds an edit
        given as prose only (the first owner feature of a project generated without one); 1 on any
        other problem.
        """;

    private static readonly TimeSpan _timeout = TimeSpan.FromSeconds(2);

    private static readonly UTF8Encoding _utf8WithBom = new(encoderShouldEmitUTF8Identifier: true);

    private static readonly Regex _editHeader = new(@"^ {1,2}(\d+)\. (.*)$", RegexOptions.None, _timeout);

    private static readonly Regex _identifier = new(@"\b[A-Z][A-Za-z0-9_]*\b", RegexOptions.None, _timeout);

    private static readonly Regex _typeDeclaration = new(
        @"^\s*(?:(?:public|internal|private|protected|sealed|static|abstract|partial|readonly)\s+)*(?:class|interface|record|enum|struct)\s+([A-Z][A-Za-z0-9_]*)",
        RegexOptions.Multiline,
        _timeout);

    private static readonly Regex _fileNamespace = new(@"^namespace\s+([A-Za-z0-9_.]+);", RegexOptions.Multiline, _timeout);

    private static readonly Regex _useCaseHeader = new(@"^Wrote \d+ files for the (?:command|query) ", RegexOptions.None, _timeout);

    private const string _apiPage = "docs/reference/api.md";

    private const string _routeSectionAnnouncement = "And its section of the route table under ## Routes in docs/reference/api.md";

    private const string _routeRowAnnouncement = "and its row in the table of ";

    private const string _httpEditHeader = "To serve it over HTTP, the edit by hand in ";

    private const string _usingContinuation = "                       ";

    private static readonly HashSet<string> _skippedFolders = new(StringComparer.Ordinal) { "bin", "obj", ".git", "TestResults" };

    private sealed record Edit(int Number, string Path, string Header, List<string> Code);

    internal static int Run(string[] arguments)
    {
        if (arguments is not [string output, string root] || arguments.Contains("--help"))
        {
            Console.WriteLine(_usage);
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
        if (output.Any(_useCaseHeader.IsMatch))
        {
            return ApplyUseCase(output, root);
        }

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
        RouteSection(root, output);

        return 0;
    }

    private static int ApplyUseCase(string[] output, string root)
    {
        int header = Array.FindIndex(output, line => line.StartsWith(_httpEditHeader, StringComparison.Ordinal));

        if (header < 0)
        {
            Console.WriteLine("The use case is served by no HTTP host here, so there is no edit to apply.");
        }
        else
        {
            ApplyAction(output, root, header);
        }

        RouteRow(root, output);

        return 0;
    }

    private static void ApplyAction(string[] output, string root, int header)
    {
        string controller = output[header][_httpEditHeader.Length..].TrimEnd(':');
        int usingsStart = Expect(output, header + 1, "  the usings it lacks  ");
        var usings = new List<string> { output[usingsStart]["  the usings it lacks  ".Length..].TrimEnd(';') };
        int index = usingsStart + 1;

        for (; index < output.Length && output[index].StartsWith(_usingContinuation, StringComparison.Ordinal); index++)
        {
            usings.Add(output[index].Trim().TrimEnd(';'));
        }

        Expect(output, index, "  the parameter        ISender sender");
        int actionStart = Expect(output, index + 1, "  the action") + 1;
        int actionEnd = Array.FindIndex(output, actionStart, line => line.StartsWith("  and ", StringComparison.Ordinal) || line.StartsWith("Then ", StringComparison.Ordinal));
        string[] action = [.. Indented(output[actionStart..(actionEnd < 0 ? output.Length : actionEnd)], "      ").Reverse().SkipWhile(line => line.Length == 0).Reverse()];

        if (action.Length == 0)
        {
            throw new InvalidOperationException("new-use-case printed no action under 'the action'.");
        }

        List<string> lines = Read(root, controller);
        foreach (string space in usings)
        {
            WithUsings(lines, [], [], space);
        }

        WithSender(lines, controller);
        int closing = lines.FindLastIndex(line => line == "}");

        if (closing < 0)
        {
            throw new InvalidOperationException($"{controller} has no closing brace at the start of a line to place the action before.");
        }

        lines.InsertRange(closing, ["", .. action.Select(line => line.Length == 0 ? line : "    " + line)]);
        Write(root, controller, lines);
        Console.WriteLine($"Applied the action: {controller}");

        for (int contract = actionEnd; contract >= 0 && output[contract].StartsWith("  and ", StringComparison.Ordinal);)
        {
            string path = output[contract]["  and ".Length..].Split(',')[0];
            int contractEnd = Array.FindIndex(output, contract + 1, line => line.StartsWith("  and ", StringComparison.Ordinal) || line.StartsWith("Then ", StringComparison.Ordinal));
            string[] body = [.. Indented(output[(contract + 1)..(contractEnd < 0 ? output.Length : contractEnd)], "      ").Reverse().SkipWhile(line => line.Length == 0).Reverse()];

            Directory.CreateDirectory(Path.GetDirectoryName(Path.Combine(root, path))!);
            Write(root, path, [.. body]);
            Console.WriteLine($"Wrote the contract: {path}");
            contract = contractEnd;
        }
    }

    private static void RouteSection(string root, string[] output)
    {
        int announced = Array.FindIndex(output, line => line.StartsWith(_routeSectionAnnouncement, StringComparison.Ordinal));

        if (announced < 0)
        {
            return;
        }

        string[] section = [.. output.Skip(announced + 1).TakeWhile(line => line.StartsWith("  ", StringComparison.Ordinal)).Select(line => line[2..]).Reverse().SkipWhile(line => line.Length == 0).Reverse()];

        if (section.Length == 0 || !section[0].StartsWith("### ", StringComparison.Ordinal))
        {
            throw new InvalidOperationException($"The output no longer prints its route section as '{_routeSectionAnnouncement}' followed by a ### section.");
        }

        List<string> lines = Read(root, _apiPage);
        int routes = lines.FindIndex(line => line == "## Routes");
        int next = routes < 0 ? -1 : lines.FindIndex(routes + 1, line => line.StartsWith("## ", StringComparison.Ordinal));

        if (next < 0)
        {
            throw new InvalidOperationException($"{_apiPage} has no '## Routes' section followed by another to take the feature's route section.");
        }

        lines.InsertRange(next, [.. section, string.Empty]);
        Write(root, _apiPage, lines);
        Console.WriteLine($"Added the feature's route section to {_apiPage}");
    }

    private static void RouteRow(string root, string[] output)
    {
        int announced = Array.FindIndex(output, line => line.StartsWith(_routeRowAnnouncement, StringComparison.Ordinal));

        if (announced < 0)
        {
            return;
        }

        string features = output[announced][_routeRowAnnouncement.Length..].Split(' ')[0];

        if (announced + 1 >= output.Length || !output[announced + 1].TrimStart().StartsWith('|'))
        {
            throw new InvalidOperationException($"The output no longer prints the route's row after '{_routeRowAnnouncement}'.");
        }

        List<string> lines = Read(root, _apiPage);
        int routes = lines.FindIndex(line => line == "## Routes");
        int end = routes < 0 ? -1 : lines.FindIndex(routes + 1, line => line.StartsWith("## ", StringComparison.Ordinal));
        string wanted = Letters(features);
        int section = routes < 0 ? -1 : lines.FindIndex(routes + 1, end < 0 ? lines.Count - routes - 1 : end - routes - 1, line => line.StartsWith("### ", StringComparison.Ordinal) && Letters(line[4..]) == wanted);
        int table = section < 0 ? -1 : lines.FindIndex(section, line => line.StartsWith("|---", StringComparison.Ordinal));

        if (table < 0)
        {
            throw new InvalidOperationException($"{_apiPage} has no route table for {features} under '## Routes' to take the use case's row.");
        }

        int last = table;

        while (last + 1 < lines.Count && lines[last + 1].StartsWith('|'))
        {
            last++;
        }

        lines.Insert(last + 1, output[announced + 1].Trim());
        Write(root, _apiPage, lines);
        Console.WriteLine($"Added the route's row to the table of {features} in {_apiPage}");
    }

    private static string Letters(string text) => new([.. text.Where(char.IsLetterOrDigit).Select(char.ToLowerInvariant)]);

    private static int Expect(string[] output, int index, string prefix) =>
        index < output.Length && output[index].StartsWith(prefix, StringComparison.Ordinal)
            ? index
            : throw new InvalidOperationException($"new-use-case no longer prints '{prefix.Trim()}' where this reads it, at line {index + 1} of its output.");

    private static IEnumerable<string> Indented(IEnumerable<string> lines, string indent) =>
        lines.Select(line => line.Length == 0
            ? line
            : line.StartsWith(indent, StringComparison.Ordinal)
                ? line[indent.Length..]
                : throw new InvalidOperationException($"'{line}' is not indented as new-use-case prints the code it gives."));

    private static void WithSender(List<string> lines, string controller)
    {
        int declaration = lines.FindIndex(line => line.Contains("Controller(", StringComparison.Ordinal) && line.Contains(" class ", StringComparison.Ordinal));
        int end = lines.FindIndex(declaration < 0 ? 0 : declaration, line => line.Contains(") :", StringComparison.Ordinal));

        if (declaration < 0 || end < 0)
        {
            throw new InvalidOperationException($"{controller} declares no controller with a primary constructor to give the ISender to.");
        }

        if (!lines.Skip(declaration).Take(end - declaration + 1).Any(line => line.Contains("ISender", StringComparison.Ordinal)))
        {
            lines.Insert(declaration + 1, "    ISender sender,");
        }
    }

    private static List<Edit> Edits(string[] output)
    {
        var edits = new List<Edit>();
        Edit? current = null;

        foreach (string line in output)
        {
            if (_editHeader.Match(line) is { Success: true } header)
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
        Regex.Match(edit.Header, pattern, RegexOptions.None, _timeout) is { Success: true } match ? match.Value : null;

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
        string method = Required(Regex.Match(edit.Header, @"an (Add\w+)\(\)", RegexOptions.None, _timeout), edit);
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
        string method = Required(Regex.Match(edit.Header, @"an (Add\w+)\(services\)", RegexOptions.None, _timeout), edit);
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
        string[] hosts = [.. Regex.Matches(edit.Header, @"Src/Presentation/[^\s,]+/Program\.cs", RegexOptions.None, _timeout)
            .Select(match => match.Value)
            .Prepend(edit.Path)
            .Distinct(StringComparer.Ordinal)];

        foreach (string host in hosts)
        {
            List<string> lines = Read(root, host);

            InsertBefore(lines, "builder.Services.AddPurgeExpiredIdempotencyKeys();", [call, string.Empty]);

            Write(root, host, WithUsings(lines, [call], declarations, ApplicationNamespace(lines, edit)));
        }
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
        Match named = Regex.Match(edit.Header, @"with using ([A-Za-z0-9_.]+);", RegexOptions.None, _timeout);

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
        string? own = lines.Select(line => _fileNamespace.Match(line)).FirstOrDefault(match => match.Success)?.Groups[1].Value;
        var needed = new SortedSet<string>(StringComparer.Ordinal);

        if (alsoNeeded is not null)
        {
            needed.Add(alsoNeeded);
        }

        foreach (Match name in code.SelectMany(text => _identifier.Matches(text)))
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

            if (_fileNamespace.Match(text) is not { Success: true } space)
            {
                continue;
            }

            foreach (Match type in _typeDeclaration.Matches(text))
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

        foreach (string child in Directory.EnumerateDirectories(folder).Where(child => !_skippedFolders.Contains(Path.GetFileName(child))))
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
        File.WriteAllText(Path.Combine(root, relative), string.Join('\n', lines) + "\n", _utf8WithBom);
}
