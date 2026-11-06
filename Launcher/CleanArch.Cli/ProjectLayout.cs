using System.Text;

namespace CleanArch.Cli;

internal static class ProjectLayout
{
    public static bool HoldsTheApi(LocatedProject project) =>
        project.File.Presentation?.Any(presentation => presentation.Framework == "api") == true;

    public static bool HasWebApps(LocatedProject project) => project.File.Web?.Apps is { Count: > 0 };

    public static string Api(LocatedProject project) =>
        Path.Combine("Src", "Presentation", $"{Initials(project)}.Api");

    public static string Persistence(LocatedProject project) =>
        Path.Combine("Src", "Infrastructure", $"{Initials(project)}.Persistence");

    public static string Auth(LocatedProject project) =>
        Path.Combine("Src", "Infrastructure", $"{Initials(project)}.Auth");

    public static IReadOnlyList<string> Contexts(LocatedProject project) =>
        project.File.Options.Auth == "local" ? [Auth(project), Persistence(project)] : [Persistence(project)];

    public static string EndToEndTests(LocatedProject project) =>
        Path.Combine("Tests", "EndToEnd", $"{Initials(project)}.EndToEnd.Tests", $"{project.File.Name}.EndToEnd.Tests.csproj");

    public static string EndToEndComposeProject(LocatedProject project) => Kebab(project.File.Name) + "-end-to-end";

    public static string SolutionName(LocatedProject project)
    {
        string byName = project.File.Name + ".sln";
        if (File.Exists(Path.Combine(project.Root, byName)))
        {
            return byName;
        }

        string byNameXml = project.File.Name + ".slnx";
        if (File.Exists(Path.Combine(project.Root, byNameXml)))
        {
            return byNameXml;
        }

        string[] any = [.. Directory.EnumerateFiles(project.Root, "*.sln")
            .Concat(Directory.EnumerateFiles(project.Root, "*.slnx"))
            .Order(StringComparer.Ordinal)];
        return any.Length > 0 ? Path.GetFileName(any[0]) : byName;
    }

    private static string Initials(LocatedProject project) =>
        string.Concat(project.File.Name.Split('.').Select(SegmentInitials));

    private static string Kebab(string name)
    {
        var kebab = new StringBuilder();
        foreach (char letter in name)
        {
            if (letter is '.' or '_' or ' ')
            {
                kebab.Append('-');
                continue;
            }

            if (char.IsUpper(letter) && kebab.Length > 0 && kebab[^1] != '-')
            {
                kebab.Append('-');
            }

            kebab.Append(char.ToLowerInvariant(letter));
        }

        return kebab.ToString();
    }

    private static string SegmentInitials(string segment) =>
        segment.Any(char.IsAsciiLetterUpper)
            ? string.Concat(segment.Where(char.IsAsciiLetterUpper))
            : string.Concat(segment.Where(char.IsAsciiLetter).Take(1).Select(char.ToUpperInvariant));
}
