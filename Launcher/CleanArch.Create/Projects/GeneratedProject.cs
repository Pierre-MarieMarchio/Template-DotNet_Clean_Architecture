using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace CleanArch.Create.Projects;

public sealed partial record GeneratedProject(
    string Root,
    string Name,
    bool HasWorkspace,
    string Scope,
    string Auth,
    string Examples,
    bool Files,
    string Database,
    bool Ci,
    bool AgentFiles)
{
    public string WebFolder => Path.Combine(Root, "Src", "Web");

    public string PresentationFolder => Path.Combine(Root, "Src", "Presentation");

    public static GeneratedProject? Find(string directory)
    {
        for (DirectoryInfo? current = new(directory); current is not null; current = current.Parent)
        {
            string[] solutions = [.. Directory.EnumerateFiles(current.FullName, "*.sln"), .. Directory.EnumerateFiles(current.FullName, "*.slnx")];
            if (solutions.Length == 1 && File.Exists(Path.Combine(current.FullName, "Tools", "Tasks.cs")))
            {
                return Read(current.FullName, Path.GetFileNameWithoutExtension(solutions[0]));
            }
        }

        return null;
    }

    private static GeneratedProject Read(string root, string name)
    {
        string web = Path.Combine(root, "Src", "Web");
        bool hasWorkspace = Directory.Exists(web);

        return new GeneratedProject(
            root,
            name,
            hasWorkspace,
            (hasWorkspace ? WorkspaceScope(Path.Combine(web, "package.json")) : null) ?? KebabName(name),
            Authentication(root, name),
            ExampleFeatures(root, name),
            Directory.Exists(Path.Combine(root, "Src", "Infrastructure", name + ".Infrastructure.Storage")),
            DatabaseProvider(root, name),
            File.Exists(Path.Combine(root, ".github", "workflows", "ci.yml")),
            File.Exists(Path.Combine(root, "AGENTS.md")));
    }

    private static string? WorkspaceScope(string packageJson)
    {
        if (!File.Exists(packageJson))
        {
            return null;
        }

        using var document = JsonDocument.Parse(File.ReadAllText(packageJson).TrimStart('﻿'));
        string? packageName = document.RootElement.TryGetProperty("name", out JsonElement value) ? value.GetString() : null;

        return packageName is { Length: > 1 } && packageName[0] == '@' && packageName.IndexOf('/', StringComparison.Ordinal) is > 1 and int slash
            ? packageName[1..slash]
            : null;
    }

    private static string KebabName(string name) => Separators().Replace(name.ToLowerInvariant(), "-");

    private static string Authentication(string root, string name)
    {
        if (Directory.Exists(Path.Combine(root, "Src", "Application", name + ".Application.Auth")))
        {
            return "local";
        }

        string packages = Path.Combine(root, "Directory.Packages.props");
        return File.Exists(packages) && File.ReadAllText(packages).Contains("Microsoft.AspNetCore.Authentication.JwtBearer", StringComparison.Ordinal)
            ? "external"
            : "none";
    }

    private static string ExampleFeatures(string root, string name)
    {
        string features = Path.Combine(root, "Src", "Application", name + ".Application", "Features");
        if (Directory.Exists(Path.Combine(features, "TodoLists")))
        {
            return "all";
        }

        return Directory.Exists(Path.Combine(features, "Notes")) ? "notes" : "none";
    }

    private static string DatabaseProvider(string root, string name)
    {
        string project = Path.Combine(root, "Src", "Infrastructure", name + ".Infrastructure.Core", name + ".Infrastructure.Core.csproj");
        string[] packages = File.Exists(project)
            ? [.. XDocument.Load(project).Descendants("PackageReference").Select(reference => (string?)reference.Attribute("Include") ?? string.Empty)]
            : [];

        if (packages.Contains("Microsoft.EntityFrameworkCore.SqlServer"))
        {
            return "sqlserver";
        }

        return packages.Contains("Microsoft.EntityFrameworkCore.Sqlite") ? "sqlite" : "postgresql";
    }

    [GeneratedRegex(@"[.\s_]+")]
    private static partial Regex Separators();
}
