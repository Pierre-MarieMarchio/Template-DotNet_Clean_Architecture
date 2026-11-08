using System.Text.Json;
using System.Xml.Linq;
namespace CleanArch.Tooling.Projects;

public static class GeneratedProject
{
    public static ProjectFacts? Find(string directory)
    {
        for (DirectoryInfo? current = new(directory); current is not null; current = current.Parent)
        {
            if (File.Exists(Path.Combine(current.FullName, "tools", "Tasks.cs")) && At(current.FullName) is { } facts)
            {
                return facts;
            }
        }

        return null;
    }

    public static ProjectFacts? At(string root)
    {
        string[] solutions = [.. Directory.EnumerateFiles(root, "*.sln"), .. Directory.EnumerateFiles(root, "*.slnx")];
        return solutions.Length == 1 ? Read(root, Path.GetFileNameWithoutExtension(solutions[0])) : null;
    }

    private static ProjectFacts Read(string root, string name)
    {
        string web = Path.Combine(root, "src", "Web");
        bool hasWorkspace = Directory.Exists(web);

        return new ProjectFacts(
            root,
            name,
            hasWorkspace,
            (hasWorkspace ? WorkspaceScope(Path.Combine(web, "package.json")) : null) ?? ProjectFacts.KebabName(name),
            Authentication(root, name),
            ExampleFeatures(root, name),
            Directory.Exists(Path.Combine(root, "src", "Infrastructure", ProjectFacts.FolderOf(name, "Infrastructure.Storage"))),
            File.Exists(Path.Combine(root, "src", "Presentation", ProjectFacts.FolderOf(name, "Presentation.Client"), name + ".Presentation.Client.csproj")),
            DatabaseProvider(root, name),
            File.Exists(Path.Combine(root, ".github", "workflows", "ci.yml")),
            File.Exists(Path.Combine(root, "AGENTS.md")),
            Directory.Exists(Path.Combine(root, "src", "Presentation", ProjectFacts.FolderOf(name, "Api"))));
    }

    public static string? WorkspaceScope(string packageJson)
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

    private static string Authentication(string root, string name)
    {
        if (Directory.Exists(Path.Combine(root, "src", "Application", ProjectFacts.FolderOf(name, "Application.Auth"))))
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
        string features = Path.Combine(root, "src", "Application", ProjectFacts.FolderOf(name, "Application"), "Features");
        if (Directory.Exists(Path.Combine(features, "TodoLists")))
        {
            return "all";
        }

        return Directory.Exists(Path.Combine(features, "Notes")) ? "notes" : "none";
    }

    private static string DatabaseProvider(string root, string name)
    {
        string project = Path.Combine(root, "src", "Infrastructure", ProjectFacts.FolderOf(name, "Infrastructure.Core"), name + ".Infrastructure.Core.csproj");
        string[] packages = File.Exists(project)
            ? [.. XDocument.Load(project).Descendants("PackageReference").Select(reference => (string?)reference.Attribute("Include") ?? string.Empty)]
            : [];

        if (packages.Contains("Microsoft.EntityFrameworkCore.SqlServer"))
        {
            return "sqlserver";
        }

        return packages.Contains("Microsoft.EntityFrameworkCore.Sqlite") ? "sqlite" : "postgresql";
    }

}
