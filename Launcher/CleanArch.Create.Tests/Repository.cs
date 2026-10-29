using CleanArch.Tooling.Manifests;

namespace CleanArch.Create.Tests;

internal static class Repository
{
    public static string Root { get; } = FindRoot();

    public static TemplateSource Source => TemplateSource.FromPath(Root);

    private static string FindRoot()
    {
        for (DirectoryInfo? directory = new(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, ".template.config", "template.json")))
            {
                return directory.FullName;
            }
        }

        throw new InvalidOperationException($"No folder above '{AppContext.BaseDirectory}' holds .template.config/template.json.");
    }
}
