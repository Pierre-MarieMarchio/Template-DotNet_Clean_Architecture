using System.Collections.ObjectModel;
using System.Text.RegularExpressions;
using CleanArch.Tooling.Additions;
using CleanArch.Tooling.Manifests;

namespace CleanArch.Create.Commands.NewProject;

internal static partial class ProjectNaming
{
    public static string? Proposed(TemplateManifest manifest, string workingDirectory)
    {
        string converted = FromFolder(Path.GetFileName(Path.TrimEndingDirectorySeparator(workingDirectory)));
        return Problem(manifest, converted) is null ? converted : manifest.DefaultName;
    }

    public static string FromFolder(string folder) =>
        string.Join('.', folder.Split('.').Select(part => string.Concat(WordSeparators().Split(part).Select(Capitalised))));

    public static string? Problem(TemplateManifest manifest, string name) =>
        Rule().IsMatch(name) ? ItemAdditions.NameRefusal(manifest, ReadOnlyDictionary<string, string>.Empty, name) : "use the letters A to Z, digits and dots, with each part starting with a letter, like Acme.Shop.";

    private static string Capitalised(string word) => word.Length == 0 ? word : char.ToUpperInvariant(word[0]) + word[1..];

    [GeneratedRegex(@"^[A-Za-z][A-Za-z0-9]*(\.[A-Za-z][A-Za-z0-9]*)*$")]
    private static partial Regex Rule();

    [GeneratedRegex(@"[\s_-]+")]
    private static partial Regex WordSeparators();
}
