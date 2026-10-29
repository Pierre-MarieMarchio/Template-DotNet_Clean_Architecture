#:property UseAppHost=false

using System.Runtime.CompilerServices;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using System.Xml.Linq;

return ProjectFileCheck.Run(args);

internal static class ProjectFileCheck
{
    private const string Usage =
        """
        Usage: dotnet run .template.config/acceptance/CheckProjectFile.cs -- <project> <name> [<options>]
               dotnet run .template.config/acceptance/CheckProjectFile.cs -- --self-test

        Checks the cleanarch.json a generation wrote at <project>'s root against the file the
        generation's name and options make: the template package and version, the name, every
        option that is not a part under the value given or its default, the client, the first web
        app and the presentation project. <options> are the cleanarch-webapi options of the leg,
        as one argument or several; their defaults and long names are read from template.json and
        dotnetcli.host.json beside this folder. The text must match too: two-space indentation, the
        contract's key order and a final newline.

        Exit code 0 when the file is the expected one; 1 when it differs, or on a malformed argument.
        """;

    private static readonly JsonSerializerOptions Written = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        NewLine = "\n",
    };

    private static readonly string[] ClientPresentations = ["avalonia", "wpf"];

    internal static int Run(string[] arguments)
    {
        if (arguments is [] or ["--help"] or ["-h"])
        {
            Console.WriteLine(Usage);
            return arguments.Length == 0 ? 1 : 0;
        }

        if (arguments is ["--self-test"])
        {
            return SelfTest();
        }

        if (arguments.Length < 2)
        {
            Console.Error.WriteLine("A project folder and the name it was generated under are required." + Environment.NewLine + Usage);
            return 1;
        }

        string expected;
        try
        {
            expected = Expected(arguments[1], [.. arguments.Skip(2).SelectMany(argument => argument.Split(' ', StringSplitOptions.RemoveEmptyEntries))]);
        }
        catch (ArgumentException error)
        {
            Console.Error.WriteLine(error.Message);
            return 1;
        }

        string path = Path.Combine(arguments[0], "cleanarch.json");
        string? written = File.Exists(path) ? File.ReadAllText(path) : null;

        if (written == expected)
        {
            Console.WriteLine($"{path} records the generation's name and options.");
            return 0;
        }

        Console.Error.WriteLine($"{path} does not record the generation's name and options. Expected:");
        Console.Error.WriteLine(expected);
        Console.Error.WriteLine(written is null ? "and the file does not exist." : "and it holds:" + Environment.NewLine + written);
        return 1;
    }

    private static string Expected(string name, string[] arguments)
    {
        string folder = Path.GetDirectoryName(Path.GetDirectoryName(SourceFile()))!;
        JsonElement symbols = JsonDocument.Parse(File.ReadAllText(Path.Combine(folder, "template.json"))).RootElement.GetProperty("symbols");
        JsonElement hosts = JsonDocument.Parse(File.ReadAllText(Path.Combine(folder, "dotnetcli.host.json"))).RootElement.GetProperty("symbolInfo");
        Dictionary<string, string> given = Given(arguments, hosts);
        string Value(string symbol) => given.GetValueOrDefault(symbol) ?? symbols.GetProperty(symbol).GetProperty("defaultValue").GetString()!;

        var options = new JsonObject();
        foreach (string option in new[] { "auth", "database", "examples", "files", "jobs", "ci", "sonar", "agentFiles" })
        {
            options[option] = symbols.GetProperty(option).GetProperty("datatype").GetString() == "bool"
                ? JsonValue.Create(bool.Parse(Value(option)))
                : JsonValue.Create(Value(option));
        }

        string web = Value("web");
        string presentation = Value("presentation");
        var project = new JsonObject
        {
            ["template"] = new JsonObject
            {
                ["package"] = XDocument.Load(Path.Combine(folder, "TemplatePackage.csproj")).Descendants("PackageId").Single().Value,
                ["version"] = symbols.GetProperty("templateVersion").GetProperty("parameters").GetProperty("value").GetString(),
            },
            ["name"] = name,
            ["options"] = options,
            ["client"] = ClientPresentations.Contains(presentation) || web == "blazor-wasm",
        };

        if (web != "none")
        {
            project["web"] = new JsonObject
            {
                ["scope"] = Regex.Replace(name.ToLowerInvariant(), @"[.\s_]+", "-", RegexOptions.None, TimeSpan.FromSeconds(1)),
                ["apps"] = new JsonArray(new JsonObject
                {
                    ["name"] = Value("webName"),
                    ["framework"] = web,
                    ["audience"] = Value("webAudience"),
                    ["rendering"] = Value("webRendering"),
                }),
                ["packages"] = new JsonArray(),
            };
        }

        if (presentation != "none")
        {
            project["presentation"] = new JsonObject
            {
                ["name"] = given.GetValueOrDefault("presentationName") ?? (presentation == "mcp" ? "Mcp" : "Portal"),
                ["framework"] = presentation,
            };
        }

        return project.ToJsonString(Written) + "\n";
    }

    private static Dictionary<string, string> Given(string[] arguments, JsonElement hosts)
    {
        Dictionary<string, string> symbolOf = hosts.EnumerateObject()
            .Where(host => host.Value.TryGetProperty("longName", out _))
            .ToDictionary(host => "--" + host.Value.GetProperty("longName").GetString(), host => host.Name, StringComparer.Ordinal);
        var given = new Dictionary<string, string>(StringComparer.Ordinal);

        for (int index = 0; index < arguments.Length; index += 2)
        {
            if (!arguments[index].StartsWith("--", StringComparison.Ordinal) || index + 1 >= arguments.Length)
            {
                throw new ArgumentException($"'{arguments[index]}' is not an option followed by its value.");
            }

            given[symbolOf.GetValueOrDefault(arguments[index]) ?? arguments[index][2..]] = arguments[index + 1];
        }

        return given;
    }

    private static int SelfTest()
    {
        string scratch = Directory.CreateTempSubdirectory("check-project-file-").FullName;
        var problems = new List<string>();

        try
        {
            string options = "--web vite-react --web-audience admin --presentation wpf --agent-files false";
            File.WriteAllText(Path.Combine(scratch, "cleanarch.json"), Expected("Acme.Shop", [.. options.Split(' ')]));

            if (Run([scratch, "Acme.Shop", options]) != 0)
            {
                problems.Add("The file the options make was refused.");
            }

            string text = File.ReadAllText(Path.Combine(scratch, "cleanarch.json"));
            string[] expectations =
            [
                "\"auth\": \"local\"", "\"agentFiles\": false", "\"client\": true", "\"scope\": \"acme-shop\"",
                "\"name\": \"Site\"", "\"audience\": \"admin\"", "\"rendering\": \"spa\"", "\"name\": \"Portal\"",
            ];
            problems.AddRange(expectations.Where(line => !text.Contains(line, StringComparison.Ordinal)).Select(line => $"The expected file lacks {line}."));

            File.WriteAllText(Path.Combine(scratch, "cleanarch.json"), text.Replace("\"agentFiles\": false", "\"agentFiles\": true", StringComparison.Ordinal));
            if (Run([scratch, "Acme.Shop", options]) != 1)
            {
                problems.Add("A file recording another option value was accepted.");
            }

            if (Run([scratch, "Acme.Shop", "--presentation"]) != 1)
            {
                problems.Add("An option without a value was accepted.");
            }
        }
        finally
        {
            Directory.Delete(scratch, recursive: true);
        }

        Console.WriteLine($"Self-test: {problems.Count} problem(s).");
        problems.ForEach(problem => Console.Error.WriteLine("  " + problem));
        return problems.Count == 0 ? 0 : 1;
    }

    private static string SourceFile([CallerFilePath] string path = "") => path;
}
