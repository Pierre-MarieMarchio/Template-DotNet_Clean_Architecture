namespace CleanArch.Cli.Generators.Shared;

internal static class DomainFolders
{
    internal static string[] Aggregates(string domain)
    {
        string repositories = Path.Combine(domain, "Repositories");

        return Directory.Exists(repositories)
            ? [.. Directory.EnumerateFiles(repositories, "I*Repository.cs")
                .Select(Path.GetFileNameWithoutExtension)
                .OfType<string>()
                .Select(file => file[1..^"Repository".Length])
                .Where(type => CSharpNames.PascalCase.IsMatch(type))
                .Order(StringComparer.Ordinal)]
            : [];
    }
}
