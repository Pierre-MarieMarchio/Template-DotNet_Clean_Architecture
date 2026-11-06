using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;

namespace CleanArch.Cli;

internal static partial class CommandSource
{
    public const string ResourcePrefix = "source/";

    private const string _entryType = "BuiltInCommands";

    private const string _namespace = "CleanArch.Cli";

    public static IReadOnlyDictionary<string, string> Shipped { get; } = ReadShipped();

    public static string TypeOf(CommandRegistration registration)
    {
        Type type = registration.Build.Method.DeclaringType
            ?? throw new InvalidOperationException("A command's registration builds it from no type.");
        while (type.DeclaringType is { } outer)
        {
            type = outer;
        }

        return type.Name;
    }

    public static IReadOnlyList<string> FilesOf(string commandType)
    {
        Dictionary<string, string> declaringFiles = Shipped
            .SelectMany(file => DeclaredTypes().Matches(file.Value).Select(match => (Type: match.Groups["type"].Value, File: file.Key)))
            .ToDictionary(declared => declared.Type, declared => declared.File, StringComparer.Ordinal);

        string seed = declaringFiles.GetValueOrDefault(commandType)
            ?? throw new InvalidOperationException($"The tool ships no source declaring {commandType}.");
        var files = new List<string> { seed };
        for (int next = 0; next < files.Count; next++)
        {
            foreach (Match identifier in Identifiers().Matches(Shipped[files[next]]))
            {
                if (declaringFiles.TryGetValue(identifier.Value, out string? file) && !files.Contains(file))
                {
                    files.Add(file);
                }
            }
        }

        return [seed, .. files.Skip(1).Order(StringComparer.Ordinal)];
    }

    public static string FileBasedApp(string commandName, string commandType, string commandLineVersion)
    {
        IReadOnlyList<string> files = FilesOf(commandType);
        string[] merged = [.. files.Select(file => FileScopedNamespace().Match(Shipped[file]).Groups["namespace"].Value)];
        string[] usings = [.. files
            .SelectMany(file => Usings().Matches(Shipped[file]).Select(match => match.Groups["namespace"].Value))
            .Except(merged, StringComparer.Ordinal)
            .Append(_namespace)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(name => name == "System" || name.StartsWith("System.", StringComparison.Ordinal) ? 0 : 1)
            .ThenBy(name => name, StringComparer.Ordinal)];

        var app = new StringBuilder();
        app.Append("#:property UseAppHost=false\n");
        app.Append("#:property PublishAot=false\n");
        app.Append($"#:property AssemblyName={RootCommandNamedApartFrom(commandName)}\n");
        app.Append("#:property ManagePackageVersionsCentrally=false\n");
        app.Append($"#:package System.CommandLine@{commandLineVersion}\n\n");
        foreach (string name in usings)
        {
            app.Append($"using {name};\n");
        }

        app.Append('\n');
        app.Append($"return CleanArchCommand.RunBuiltIn([\"{commandName}\", .. args], Environment.CurrentDirectory, Console.Out, Console.Error, {_entryType}.All);\n\n");
        app.Append($"namespace {_namespace}\n{{\n");
        app.Append(Indented($"internal static class {_entryType}\n{{\n    public static IReadOnlyList<CommandRegistration> All {{ get; }} = [{commandType}.Registration];\n}}\n"));
        foreach (string file in files)
        {
            app.Append('\n').Append(Indented(BodyOf(Shipped[file])));
        }

        app.Append("}\n");
        return app.ToString();
    }

    private static string RootCommandNamedApartFrom(string commandName) => $"dotnet-cleanarch-{commandName}";

    private static string BodyOf(string source)
    {
        string withoutUsings = Usings().Replace(source, string.Empty);
        return FileScopedNamespace().Replace(withoutUsings, string.Empty).Trim('\n') + "\n";
    }

    private static string Indented(string code) =>
        string.Concat(code.Split('\n').SkipLast(1).Select(line => line.Length == 0 ? "\n" : $"    {line}\n"));

    private static Dictionary<string, string> ReadShipped()
    {
        Assembly tool = typeof(CommandSource).Assembly;
        var shipped = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (string resource in tool.GetManifestResourceNames().Where(name => name.StartsWith(ResourcePrefix, StringComparison.Ordinal)))
        {
            using Stream stream = tool.GetManifestResourceStream(resource)!;
            using var reader = new StreamReader(stream, Encoding.UTF8);
            shipped[resource[ResourcePrefix.Length..]] = reader.ReadToEnd().TrimStart('\uFEFF').ReplaceLineEndings("\n");
        }

        return shipped;
    }

    [GeneratedRegex(@"^(?:(?:public|internal|private|file|sealed|static|abstract|partial|readonly)\s+)*(?:class|record|struct|interface|enum)\s+(?<type>\w+)", RegexOptions.Multiline)]
    private static partial Regex DeclaredTypes();

    [GeneratedRegex(@"\b[A-Z]\w*\b")]
    private static partial Regex Identifiers();

    [GeneratedRegex(@"^using (?<namespace>[\w.]+);\n", RegexOptions.Multiline)]
    private static partial Regex Usings();

    [GeneratedRegex(@"^namespace (?<namespace>[\w.]+);\n", RegexOptions.Multiline)]
    private static partial Regex FileScopedNamespace();
}
