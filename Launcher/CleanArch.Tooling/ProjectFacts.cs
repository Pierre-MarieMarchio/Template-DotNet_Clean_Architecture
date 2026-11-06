namespace CleanArch.Tooling;

public sealed record ProjectFacts(
    string Root,
    string Name,
    bool HasWorkspace,
    string Scope,
    string Auth,
    string Examples,
    bool Files,
    bool HasClient,
    string Database,
    bool Ci,
    bool AgentFiles,
    bool HasApi)
{
    public string WebFolder => Path.Combine(Root, "Src", "Web");

    public string PresentationFolder => Path.Combine(Root, "Src", "Presentation");

    public string FolderOf(string part) => FolderOf(Name, part);

    public string? BlazorWebAssemblyApp => Directory.Exists(Path.Combine(WebFolder, "apps"))
        ? Directory.EnumerateDirectories(Path.Combine(WebFolder, "apps"))
            .Where(app => Directory.EnumerateFiles(app, "*.csproj").Any())
            .Select(Path.GetFileName)
            .Order(StringComparer.Ordinal)
            .FirstOrDefault()
        : null;

    public static string FolderOf(string name, string part) => $"{string.Concat(name.Split('.').Select(SegmentInitials))}.{part}";

    private static string SegmentInitials(string segment) =>
        segment.Any(char.IsAsciiLetterUpper)
            ? string.Concat(segment.Where(char.IsAsciiLetterUpper))
            : string.Concat(segment.Where(char.IsAsciiLetter).Take(1).Select(char.ToUpperInvariant));
}
