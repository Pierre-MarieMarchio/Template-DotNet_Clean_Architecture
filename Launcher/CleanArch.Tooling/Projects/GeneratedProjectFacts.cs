using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace CleanArch.Tooling.Projects;

public sealed record TemplateProvenance(string Package, string Version);

public sealed record GeneratedWebApp(string Name, string? Framework, string Audience, string Rendering, int? Port);

public sealed record GeneratedPresentation(string Name, string? Framework);

public static partial class GeneratedProjectFacts
{
    private const string _scaffoldDescriptor = "scaffold.json";

    private static readonly string[] _workspacePackages = ["api-client", "browser-smoke", "eslint-config", "tsconfig"];

    private static readonly Dictionary<string, string> _viteTemplates = new(StringComparer.Ordinal)
    {
        ["react-ts"] = "vite-react",
        ["vue-ts"] = "vite-vue",
        ["svelte-ts"] = "vite-svelte",
    };

    private static readonly (string Dependency, string Framework)[] _viteDependencies =
    [
        ("react", "vite-react"),
        ("vue", "vite-vue"),
        ("svelte", "vite-svelte"),
    ];

    public static bool Sonar(ProjectFacts project) => File.Exists(Path.Combine(project.Root, "Tools", "sonar-scanner.Dockerfile"));

    public static bool Aspire(ProjectFacts project) => Directory.Exists(Path.Combine(project.Root, "Src", "AppHost", project.FolderOf("AppHost")));

    public static bool FoldersCarryTheInitials(ProjectFacts project) => Directory.Exists(Path.Combine(project.Root, "Src", "Domain", project.FolderOf("Domain")));

    public static TemplateProvenance? Provenance(string root)
    {
        string overview = Path.Combine(root, "docs", "getting-started", "overview.md");
        Match sentence = File.Exists(overview) ? ProvenanceSentence().Match(File.ReadAllText(overview)) : Match.Empty;
        return sentence.Success ? new TemplateProvenance(sentence.Groups["package"].Value, sentence.Groups["version"].Value) : null;
    }

    public static IReadOnlyList<GeneratedWebApp> WebApps(string web) =>
        [.. Folders(Path.Combine(web, "apps")).Select(app => WebApp(Path.Combine(web, "apps", app)))];

    public static IReadOnlyList<string> WebPackages(string web) =>
        [.. Folders(Path.Combine(web, "packages")).Except(_workspacePackages, StringComparer.Ordinal)];

    public static IReadOnlyList<GeneratedPresentation> Presentations(ProjectFacts project) =>
        [.. Folders(project.PresentationFolder)
            .Where(folder => folder != project.FolderOf("Presentation.Core") && folder != project.FolderOf("Presentation.Client"))
            .Select(folder => Presentation(project, folder))];

    private static GeneratedWebApp WebApp(string folder)
    {
        string? framework = WebFramework(folder);
        string rendering = framework == "blazor-wasm" || File.Exists(Path.Combine(folder, "nginx.conf")) ? "spa" : "ssr";
        return new GeneratedWebApp(Path.GetFileName(folder), framework, WebAudience(folder), rendering, WebPort(folder));
    }

    private static string? WebFramework(string folder)
    {
        if (Directory.EnumerateFiles(folder, "*.csproj").Any())
        {
            return "blazor-wasm";
        }

        using (JsonDocument? descriptor = ReadJson(Path.Combine(folder, _scaffoldDescriptor)))
        {
            if (descriptor is not null)
            {
                return ScaffoldedWebFramework(descriptor.RootElement);
            }
        }

        if (File.Exists(Path.Combine(folder, "angular.json")))
        {
            return "angular";
        }

        if (File.Exists(Path.Combine(folder, "react-router.config.ts")))
        {
            return "react-router";
        }

        using JsonDocument? package = ReadJson(Path.Combine(folder, "package.json"));
        if (package is null)
        {
            return null;
        }

        string[] dependencies = [.. Dependencies(package.RootElement, "dependencies"), .. Dependencies(package.RootElement, "devDependencies")];
        if (dependencies.Length == 0)
        {
            return "placeholder";
        }

        return _viteDependencies.FirstOrDefault(vite => dependencies.Contains(vite.Dependency, StringComparer.Ordinal)).Framework;
    }

    private static string? ScaffoldedWebFramework(JsonElement descriptor)
    {
        if (Text(descriptor, "kind") == "dotnet")
        {
            return "blazor-wasm";
        }

        string[] arguments = descriptor.TryGetProperty("arguments", out JsonElement list) && list.ValueKind == JsonValueKind.Array
            ? [.. list.EnumerateArray().Select(argument => argument.GetString() ?? string.Empty)]
            : [];

        return Text(descriptor, "package") switch
        {
            "@angular/cli" => "angular",
            "create-react-router" => "react-router",
            "create-vite" => arguments.SkipWhile(argument => argument != "--template").Skip(1).Select(template => _viteTemplates.GetValueOrDefault(template)).FirstOrDefault(),
            _ => null,
        };
    }

    private static string WebAudience(string folder)
    {
        string readme = Path.Combine(folder, "README.md");
        return File.Exists(readme) && File.ReadAllText(readme).Contains("back office over the API, for administrators", StringComparison.Ordinal)
            ? "admin"
            : "public";
    }

    private static int? WebPort(string folder)
    {
        string compose = Path.Combine(folder, "compose.yml");
        Match port = File.Exists(compose) ? HostPort().Match(File.ReadAllText(compose)) : Match.Empty;
        return port.Success ? int.Parse(port.Groups["port"].Value, System.Globalization.CultureInfo.InvariantCulture) : null;
    }

    private static GeneratedPresentation Presentation(ProjectFacts project, string folder)
    {
        string path = Path.Combine(project.PresentationFolder, folder);
        string? csproj = Directory.EnumerateFiles(path, "*.csproj").Order(StringComparer.Ordinal).FirstOrDefault();
        string projectName = csproj is null ? folder : Path.GetFileNameWithoutExtension(csproj);
        string name = projectName.StartsWith(project.Name + ".", StringComparison.Ordinal)
            ? projectName[(project.Name.Length + 1)..]
            : folder[(folder.IndexOf('.', StringComparison.Ordinal) + 1)..];

        if (folder == project.FolderOf("Api"))
        {
            return new GeneratedPresentation(name, "api");
        }

        using JsonDocument? descriptor = ReadJson(Path.Combine(path, _scaffoldDescriptor));
        return new GeneratedPresentation(name, descriptor is null ? PresentationFramework(csproj) : Text(descriptor.RootElement, "framework"));
    }

    private static string? PresentationFramework(string? csproj)
    {
        if (csproj is null)
        {
            return null;
        }

        XDocument project = XDocument.Load(csproj);
        string sdk = (string?)project.Root?.Attribute("Sdk") ?? string.Empty;
        string[] packages = [.. project.Descendants("PackageReference").Select(reference => (string?)reference.Attribute("Include") ?? string.Empty)];

        if (sdk == "Microsoft.NET.Sdk.Worker")
        {
            return "worker";
        }

        if (project.Descendants("UseWPF").Any(element => element.Value.Trim() == "true"))
        {
            return "wpf";
        }

        if (packages.Contains("Avalonia"))
        {
            return "avalonia";
        }

        if (packages.Any(package => package.StartsWith("ModelContextProtocol", StringComparison.Ordinal)))
        {
            return "mcp";
        }

        return sdk == "Microsoft.NET.Sdk.Web" ? "blazor-server" : null;
    }

    private static IEnumerable<string> Dependencies(JsonElement package, string key) =>
        package.TryGetProperty(key, out JsonElement dependencies) && dependencies.ValueKind == JsonValueKind.Object
            ? dependencies.EnumerateObject().Select(dependency => dependency.Name)
            : [];

    private static string? Text(JsonElement json, string key) =>
        json.TryGetProperty(key, out JsonElement value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    private static JsonDocument? ReadJson(string path) =>
        File.Exists(path)
            ? JsonDocument.Parse(File.ReadAllText(path).TrimStart('﻿'), new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true })
            : null;

    private static IEnumerable<string> Folders(string folder) =>
        Directory.Exists(folder)
            ? Directory.EnumerateDirectories(folder).Select(Path.GetFileName).OfType<string>().Order(StringComparer.Ordinal)
            : [];

    [GeneratedRegex(@"This project was generated by version\s+(?<version>\S+)\s+of\s+`(?<package>[^`]+)`")]
    private static partial Regex ProvenanceSentence();

    [GeneratedRegex(@"_HOST_PORT:-(?<port>\d+)\}")]
    private static partial Regex HostPort();
}
